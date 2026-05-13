using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxImageControlReplacer
    {
        private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf/";
        private static readonly string[] GraphicalObjectNames =
        {
            "pic",
            "container",
            "line",
            "rect",
            "ellipse",
            "arc",
            "polygon",
            "curve",
            "ole",
            "chart"
        };

        public static ImageReplacementResult Replace(ReplaceImageControlOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ValidateOptions(options);

            var result = new ImageReplacementResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Target = options.Target ?? string.Empty,
                SourceImage = ImageInfo.Empty
            };

            try
            {
                result.SourceImage = ReadImageInfoFromFile(options.ImagePath);
            }
            catch (Exception ex)
            {
                return result.Fail("replacement image could not be read: " + ex.GetType().Name + ": " + ex.Message);
            }

            var selector = TargetSelector.Parse(options.Target);
            var entries = SimpleZipArchive.ReadAll(options.InputPath);
            var beforeInventory = HwpxPictureInspector.Inspect(options.InputPath);
            var beforeScan = HwpxFeatureScanner.Scan(options.InputPath);
            result.PictureCountBefore = beforeInventory.PictureCount;
            result.TableCountBefore = beforeScan.Count("tables");

            var packageKey = FindContentPackagePath(entries);
            if (string.IsNullOrWhiteSpace(packageKey))
            {
                return result.Fail("content.hpf was not found");
            }

            var xmlDocuments = ReadXmlDocuments(entries, packageKey);
            XDocument package;
            if (!xmlDocuments.TryGetValue(packageKey, out package))
            {
                return result.Fail("content.hpf could not be parsed");
            }

            var referenceMap = BuildImageReferenceMap(xmlDocuments);
            var pictures = LocatePictures(entries, xmlDocuments, referenceMap).ToList();
            var matches = pictures.Where(item => selector.Matches(item)).ToList();
            if (matches.Count == 0)
            {
                return result.Fail("target picture was not found");
            }

            if (matches.Count > 1)
            {
                return result.Fail("target selector matched more than one picture; use control:gso:<index> or picture:<index>");
            }

            var target = matches[0];
            result.TargetPart = target.PartPath;
            result.TargetPictureIndex = target.PictureIndex;
            result.TargetPackageGsoIndex = target.PackageGsoIndex;
            result.ImageReference = target.ImageReference;
            result.BeforeProperties = target.Properties;

            if (string.IsNullOrWhiteSpace(target.ImageReference))
            {
                return result.Fail("target picture has no image reference");
            }

            if (pictures.Count(item => string.Equals(item.ImageReference, target.ImageReference, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                return result.Fail("target image reference is shared by multiple pictures; package replacement would affect more than one object");
            }

            if (string.IsNullOrWhiteSpace(target.BinDataPath))
            {
                return result.Fail("target picture BinData path could not be resolved");
            }

            if (pictures.Count(item => string.Equals(NormalizePackagePath(item.BinDataPath), NormalizePackagePath(target.BinDataPath), StringComparison.OrdinalIgnoreCase)) > 1)
            {
                return result.Fail("target BinData path is shared by multiple pictures; package replacement would affect more than one object");
            }

            byte[] beforeBytes;
            if (!TryGetEntry(entries, target.BinDataPath, out beforeBytes))
            {
                return result.Fail("target BinData entry was not found: " + target.BinDataPath);
            }

            result.BeforeImage = ReadImageInfoFromBytes(target.BinDataPath, beforeBytes);

            var sourceBytes = File.ReadAllBytes(options.ImagePath);
            var newExtension = NormalizeImageExtension(Path.GetExtension(options.ImagePath));
            if (!IsSupportedImageExtension(newExtension))
            {
                return result.Fail("replacement image extension is not supported: " + (Path.GetExtension(options.ImagePath) ?? string.Empty));
            }

            var oldExtension = NormalizeImageExtension(Path.GetExtension(target.BinDataPath));
            var desiredPath = string.Equals(newExtension, oldExtension, StringComparison.OrdinalIgnoreCase)
                ? target.BinDataPath
                : "BinData/" + target.ImageReference + newExtension;
            var replacementPath = UniqueBinDataPath(entries, desiredPath, target.BinDataPath);

            entries[replacementPath] = sourceBytes;
            result.AfterBinDataPath = replacementPath;
            result.OldBinDataRetained = !string.Equals(replacementPath, target.BinDataPath, StringComparison.OrdinalIgnoreCase);

            UpdatePackageManifest(package, target.ImageReference, replacementPath, MediaTypeForExtension(newExtension));
            var touchedHeaders = UpdateHeaderBinData(xmlDocuments, target.ImageReference, replacementPath);

            entries[packageKey] = SerializeXml(package);
            foreach (var headerPath in touchedHeaders)
            {
                entries[headerPath] = SerializeXml(xmlDocuments[headerPath]);
            }

            SimpleZipArchive.WriteAllPreservingTemplate(options.InputPath, options.OutputPath, entries);

            VerifyAfterWrite(result, selector, options);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                result.LayoutReportPath = AppendSuffix(options.ReportPath, ".layout.md");
                result.LayoutPassed = HwpxLayoutValidator.Validate(options.InputPath, options.OutputPath, result.LayoutReportPath);
            }
            else
            {
                result.LayoutSkippedReason = "report path was not provided";
            }

            if (!result.HashVerified)
            {
                return result.Fail("after BinData SHA256 did not match source image");
            }

            if (result.PictureCountBefore != result.PictureCountAfter)
            {
                return result.Fail("picture count changed");
            }

            if (result.TableCountBefore != result.TableCountAfter)
            {
                return result.Fail("table count changed");
            }

            if (!result.PropertiesPreserved)
            {
                return result.Fail("target picture properties changed");
            }

            result.Success = true;
            result.Verdict = "applied";
            return result;
        }

        public static void WriteMarkdown(ImageReplacementResult result, string reportPath)
        {
            if (result == null || string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("# HWPX Image Replacement Report");
            builder.AppendLine();
            builder.AppendLine("- verdict: " + result.Verdict);
            if (!string.IsNullOrWhiteSpace(result.FailureReason))
            {
                builder.AppendLine("- failure: " + result.FailureReason);
            }

            builder.AppendLine("- input: " + result.InputPath);
            builder.AppendLine("- output: " + result.OutputPath);
            builder.AppendLine("- target: " + result.Target);
            builder.AppendLine("- target part: " + result.TargetPart);
            builder.AppendLine("- target picture index: " + FormatInt(result.TargetPictureIndex));
            builder.AppendLine("- target package gso index: " + FormatInt(result.TargetPackageGsoIndex));
            builder.AppendLine("- image reference: " + result.ImageReference);
            builder.AppendLine("- before BinData: " + result.BeforeImage.Path);
            builder.AppendLine("- after BinData: " + result.AfterImage.Path);
            builder.AppendLine("- old BinData retained: " + (result.OldBinDataRetained ? "yes" : "no"));
            builder.AppendLine("- picture count: " + result.PictureCountBefore.ToString(CultureInfo.InvariantCulture) + " -> " + result.PictureCountAfter.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- table count: " + result.TableCountBefore.ToString(CultureInfo.InvariantCulture) + " -> " + result.TableCountAfter.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- hash verified: " + (result.HashVerified ? "yes" : "no"));
            builder.AppendLine("- properties preserved: " + (result.PropertiesPreserved ? "yes" : "no"));
            if (result.LayoutPassed.HasValue)
            {
                builder.AppendLine("- validate-layout: " + (result.LayoutPassed.Value ? "passed" : "failed"));
                builder.AppendLine("- validate-layout report: " + result.LayoutReportPath);
            }
            else
            {
                builder.AppendLine("- validate-layout: skipped");
                builder.AppendLine("- validate-layout skipped reason: " + result.LayoutSkippedReason);
            }

            builder.AppendLine();
            builder.AppendLine("## Source Image");
            AppendImageInfo(builder, result.SourceImage);
            builder.AppendLine();
            builder.AppendLine("## Before Image");
            AppendImageInfo(builder, result.BeforeImage);
            builder.AppendLine();
            builder.AppendLine("## After Image");
            AppendImageInfo(builder, result.AfterImage);
            builder.AppendLine();
            builder.AppendLine("## Preserved Picture Properties");
            builder.AppendLine();
            builder.AppendLine("| property | before | after | preserved |");
            builder.AppendLine("| --- | --- | --- | --- |");
            foreach (var item in result.PropertyComparisons)
            {
                builder.Append("| ");
                builder.Append(EscapeMarkdownTable(item.Name));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(item.Before));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(item.After));
                builder.Append(" | ");
                builder.Append(item.Preserved ? "yes" : "no");
                builder.AppendLine(" |");
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, builder.ToString(), new UTF8Encoding(true));
        }

        private static void ValidateOptions(ReplaceImageControlOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.InputPath))
            {
                throw new ArgumentException("input HWPX path is required");
            }

            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                throw new ArgumentException("output HWPX path is required");
            }

            if (string.IsNullOrWhiteSpace(options.Target))
            {
                throw new ArgumentException("--target is required");
            }

            if (string.IsNullOrWhiteSpace(options.ImagePath))
            {
                throw new ArgumentException("--image is required");
            }

            if (!File.Exists(options.InputPath))
            {
                throw new FileNotFoundException("input HWPX was not found", options.InputPath);
            }

            if (!File.Exists(options.ImagePath))
            {
                throw new FileNotFoundException("replacement image was not found", options.ImagePath);
            }

            if (string.Equals(Path.GetFullPath(options.InputPath), Path.GetFullPath(options.OutputPath), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("input and output paths must be different so before/after validation can compare both files");
            }
        }

        private static void VerifyAfterWrite(ImageReplacementResult result, TargetSelector selector, ReplaceImageControlOptions options)
        {
            var afterInventory = HwpxPictureInspector.Inspect(options.OutputPath);
            var afterScan = HwpxFeatureScanner.Scan(options.OutputPath);
            result.PictureCountAfter = afterInventory.PictureCount;
            result.TableCountAfter = afterScan.Count("tables");

            var afterMatches = afterInventory.Files
                .SelectMany(file => file.Pictures)
                .Where(item => selector.Matches(item))
                .ToList();
            if (afterMatches.Count != 1)
            {
                result.AfterImage = ImageInfo.Empty;
                result.AfterProperties = PictureProperties.Empty;
                result.PropertyComparisons = result.BeforeProperties.CompareTo(result.AfterProperties);
                return;
            }

            var after = afterMatches[0];
            result.AfterImage = new ImageInfo
            {
                Path = after.BinDataPath,
                ImageType = after.ImageType,
                PixelSize = after.PixelSize,
                ByteSize = after.ByteSize,
                Sha256 = after.Sha256
            };
            result.AfterProperties = PictureProperties.FromInventory(after);
            result.PropertyComparisons = result.BeforeProperties.CompareTo(result.AfterProperties);
            result.HashVerified = string.Equals(result.SourceImage.Sha256, result.AfterImage.Sha256, StringComparison.OrdinalIgnoreCase);
            result.PropertiesPreserved = result.PropertyComparisons.All(item => item.Preserved);
        }

        private static IDictionary<string, XDocument> ReadXmlDocuments(IDictionary<string, byte[]> entries, string packageKey)
        {
            var result = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
            AddXmlDocument(entries, result, packageKey);
            AddXmlDocument(entries, result, "Contents/header.xml");
            AddXmlDocument(entries, result, "header.xml");
            foreach (var partPath in ResolvePicturePartPaths(entries.Keys))
            {
                AddXmlDocument(entries, result, partPath);
            }

            return result;
        }

        private static void AddXmlDocument(IDictionary<string, byte[]> entries, IDictionary<string, XDocument> result, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || result.ContainsKey(path))
            {
                return;
            }

            byte[] data;
            if (!entries.TryGetValue(path, out data))
            {
                return;
            }

            result[path] = XDocument.Parse(Encoding.UTF8.GetString(data), LoadOptions.PreserveWhitespace);
        }

        private static IList<PictureMatch> LocatePictures(
            IDictionary<string, byte[]> entries,
            IDictionary<string, XDocument> xmlDocuments,
            IDictionary<string, string> referenceMap)
        {
            var result = new List<PictureMatch>();
            var gsoIndex = -1;
            var pictureIndex = 0;
            foreach (var partPath in ResolvePicturePartPaths(entries.Keys))
            {
                XDocument document;
                if (!xmlDocuments.TryGetValue(partPath, out document))
                {
                    continue;
                }

                foreach (var element in document.Descendants())
                {
                    if (IsGraphicalObject(element))
                    {
                        gsoIndex++;
                    }

                    if (!string.Equals(element.Name.LocalName, "pic", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var imageElement = element.Descendants().FirstOrDefault(item => string.Equals(item.Name.LocalName, "img", StringComparison.Ordinal));
                    var imageReference = GetAttribute(imageElement, "binaryItemIDRef");
                    if (string.IsNullOrWhiteSpace(imageReference))
                    {
                        imageReference = GetAttribute(imageElement, "binItem");
                    }

                    if (string.IsNullOrWhiteSpace(imageReference))
                    {
                        imageReference = GetAttribute(imageElement, "href");
                    }

                    result.Add(new PictureMatch
                    {
                        PartPath = partPath,
                        Picture = element,
                        PictureIndex = pictureIndex,
                        PackageGsoIndex = gsoIndex,
                        ImageElement = imageElement,
                        ImageReference = imageReference,
                        BinDataPath = ResolveBinDataPath(imageReference, referenceMap, entries.Keys),
                        Properties = PictureProperties.FromElement(element)
                    });
                    pictureIndex++;
                }
            }

            return result;
        }

        private static IDictionary<string, string> BuildImageReferenceMap(IDictionary<string, XDocument> xmlDocuments)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in xmlDocuments)
            {
                if (!string.Equals(pair.Key, "Contents/header.xml", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Key, "header.xml", StringComparison.OrdinalIgnoreCase) &&
                    !pair.Key.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var element in pair.Value.Descendants())
                {
                    if (!string.Equals(element.Name.LocalName, "binData", StringComparison.Ordinal) &&
                        !string.Equals(element.Name.LocalName, "item", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var id = GetAttribute(element, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var href = GetAttribute(element, "href");
                    if (string.IsNullOrWhiteSpace(href))
                    {
                        href = GetAttribute(element, "path");
                    }

                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        map[id] = ResolvePackageHref(pair.Key, href);
                    }
                }
            }

            return map;
        }

        private static string ResolveBinDataPath(string imageReference, IDictionary<string, string> referenceMap, IEnumerable<string> entryPaths)
        {
            if (string.IsNullOrWhiteSpace(imageReference))
            {
                return string.Empty;
            }

            string mapped;
            if (referenceMap.TryGetValue(imageReference, out mapped))
            {
                var normalized = NormalizePackagePath(mapped);
                if (entryPaths.Any(path => string.Equals(NormalizePackagePath(path), normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    return normalized;
                }
            }

            if (imageReference.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizePackagePath(imageReference);
            }

            return entryPaths
                .Select(NormalizePackagePath)
                .FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), imageReference, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static void UpdatePackageManifest(XDocument package, string imageId, string href, string mediaType)
        {
            var manifest = package.Descendants(Opf + "manifest").FirstOrDefault()
                ?? package.Descendants().FirstOrDefault(item => string.Equals(item.Name.LocalName, "manifest", StringComparison.Ordinal));
            if (manifest == null)
            {
                return;
            }

            var item = manifest.Elements()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "item", StringComparison.Ordinal) &&
                                           string.Equals(GetAttribute(element, "id"), imageId, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                item = new XElement(manifest.Name.Namespace + "item", new XAttribute("id", imageId));
                manifest.Add(item);
            }

            SetAttribute(item, "href", href);
            SetAttribute(item, "media-type", mediaType);
            SetAttribute(item, "isEmbeded", "1");
        }

        private static IList<string> UpdateHeaderBinData(IDictionary<string, XDocument> xmlDocuments, string imageId, string href)
        {
            var touched = new List<string>();
            foreach (var key in new[] { "Contents/header.xml", "header.xml" })
            {
                XDocument document;
                if (!xmlDocuments.TryGetValue(key, out document))
                {
                    continue;
                }

                foreach (var element in document.Descendants().Where(item => string.Equals(item.Name.LocalName, "binData", StringComparison.Ordinal)))
                {
                    if (!string.Equals(GetAttribute(element, "id"), imageId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (HasAttribute(element, "href"))
                    {
                        SetAttribute(element, "href", href);
                    }
                    else if (HasAttribute(element, "path"))
                    {
                        SetAttribute(element, "path", href);
                    }
                    else
                    {
                        SetAttribute(element, "href", href);
                    }

                    if (!touched.Contains(key))
                    {
                        touched.Add(key);
                    }
                }
            }

            return touched;
        }

        private static IEnumerable<string> ResolvePicturePartPaths(IEnumerable<string> paths)
        {
            return paths
                .Select(NormalizePackagePath)
                .Where(path => path.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase))
                .Where(path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(path, "Contents/header.xml", StringComparison.OrdinalIgnoreCase))
                .Where(path => !string.Equals(path, "Contents/content.hpf", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => IsSectionPart(path) ? 0 : 1)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsSectionPart(string path)
        {
            var fileName = Path.GetFileName(path);
            return path.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                   fileName.StartsWith("section", StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGraphicalObject(XElement element)
        {
            return element != null && GraphicalObjectNames.Any(name => string.Equals(element.Name.LocalName, name, StringComparison.Ordinal));
        }

        private static string FindContentPackagePath(IDictionary<string, byte[]> entries)
        {
            if (entries.ContainsKey("Contents/content.hpf"))
            {
                return "Contents/content.hpf";
            }

            if (entries.ContainsKey("content.hpf"))
            {
                return "content.hpf";
            }

            return entries.Keys.FirstOrDefault(path => path.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static string ResolvePackageHref(string xmlPath, string href)
        {
            var normalizedHref = NormalizePackagePath(href);
            if (normalizedHref.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedHref;
            }

            if (normalizedHref.StartsWith("../", StringComparison.Ordinal))
            {
                return NormalizePackagePath(normalizedHref.Substring(3));
            }

            if (xmlPath.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) && normalizedHref.IndexOf('/') < 0)
            {
                return "BinData/" + normalizedHref;
            }

            return normalizedHref;
        }

        private static string UniqueBinDataPath(IDictionary<string, byte[]> entries, string desiredPath, string oldPath)
        {
            var normalizedDesired = NormalizePackagePath(desiredPath);
            var normalizedOld = NormalizePackagePath(oldPath);
            if (!entries.Keys.Any(path => string.Equals(NormalizePackagePath(path), normalizedDesired, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(normalizedDesired, normalizedOld, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedDesired;
            }

            var directory = Path.GetDirectoryName(normalizedDesired.Replace('/', Path.DirectorySeparatorChar)) ?? "BinData";
            directory = NormalizePackagePath(directory);
            var stem = Path.GetFileNameWithoutExtension(normalizedDesired);
            var extension = Path.GetExtension(normalizedDesired);
            for (var index = 1; index < 1000; index++)
            {
                var candidate = NormalizePackagePath(directory + "/" + stem + "_replacement" + index.ToString(CultureInfo.InvariantCulture) + extension);
                if (!entries.Keys.Any(path => string.Equals(NormalizePackagePath(path), candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Could not allocate a unique BinData path for replacement image.");
        }

        private static bool TryGetEntry(IDictionary<string, byte[]> entries, string path, out byte[] data)
        {
            if (entries.TryGetValue(path, out data))
            {
                return true;
            }

            var match = entries.Keys.FirstOrDefault(item => string.Equals(NormalizePackagePath(item), NormalizePackagePath(path), StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                data = null;
                return false;
            }

            data = entries[match];
            return true;
        }

        private static ImageInfo ReadImageInfoFromFile(string path)
        {
            var data = File.ReadAllBytes(path);
            var info = ReadImageInfoFromBytes(path, data, true);
            info.Path = Path.GetFullPath(path);
            return info;
        }

        private static ImageInfo ReadImageInfoFromBytes(string path, byte[] data)
        {
            return ReadImageInfoFromBytes(path, data, false);
        }

        private static ImageInfo ReadImageInfoFromBytes(string path, byte[] data, bool requireDecodableImage)
        {
            var info = new ImageInfo
            {
                Path = path ?? string.Empty,
                ImageType = ImageTypeFromExtension(path),
                ByteSize = data == null ? 0 : data.Length,
                Sha256 = ComputeSha256(data ?? new byte[0])
            };

            try
            {
                using (var stream = new MemoryStream(data ?? new byte[0]))
                using (var image = Image.FromStream(stream, false, false))
                {
                    info.PixelSize = image.Width.ToString(CultureInfo.InvariantCulture) + "x" + image.Height.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                if (requireDecodableImage)
                {
                    throw;
                }

                info.PixelSize = string.Empty;
            }

            return info;
        }

        private static string ImageTypeFromExtension(string path)
        {
            return (Path.GetExtension(path) ?? string.Empty).TrimStart('.').ToLowerInvariant();
        }

        private static string NormalizeImageExtension(string extension)
        {
            var value = (extension ?? string.Empty).Trim().ToLowerInvariant();
            if (value == ".jpg")
            {
                return ".jpeg";
            }

            return value;
        }

        private static bool IsSupportedImageExtension(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".png":
                case ".jpeg":
                case ".bmp":
                case ".gif":
                    return true;
                default:
                    return false;
            }
        }

        private static string MediaTypeForExtension(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".jpeg":
                case ".jpg":
                    return "image/jpeg";
                case ".bmp":
                    return "image/bmp";
                case ".gif":
                    return "image/gif";
                case ".png":
                default:
                    return "image/png";
            }
        }

        private static string ComputeSha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data ?? new byte[0])).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static XElement FirstDescendant(XElement element, string localName)
        {
            return element == null
                ? null
                : element.Descendants().FirstOrDefault(item => string.Equals(item.Name.LocalName, localName, StringComparison.Ordinal));
        }

        private static string FormatAttributes(XElement element, params string[] names)
        {
            if (element == null)
            {
                return string.Empty;
            }

            var values = new List<string>();
            foreach (var name in names)
            {
                var value = GetAttribute(element, name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(name + "=" + value);
                }
            }

            return string.Join("; ", values.ToArray());
        }

        private static string GetAttribute(XElement element, string localName)
        {
            if (element == null)
            {
                return string.Empty;
            }

            var attribute = element.Attributes().FirstOrDefault(item => string.Equals(item.Name.LocalName, localName, StringComparison.Ordinal));
            return attribute == null ? string.Empty : attribute.Value ?? string.Empty;
        }

        private static bool HasAttribute(XElement element, string localName)
        {
            return element != null && element.Attributes().Any(item => string.Equals(item.Name.LocalName, localName, StringComparison.Ordinal));
        }

        private static void SetAttribute(XElement element, string localName, string value)
        {
            var attribute = element.Attributes().FirstOrDefault(item => string.Equals(item.Name.LocalName, localName, StringComparison.Ordinal));
            if (attribute == null)
            {
                element.Add(new XAttribute(localName, value ?? string.Empty));
            }
            else
            {
                attribute.Value = value ?? string.Empty;
            }
        }

        private static string NormalizePackagePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }

        private static string AppendSuffix(string path, string suffix)
        {
            var directory = Path.GetDirectoryName(path);
            var fileName = Path.GetFileNameWithoutExtension(path);
            return Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory, fileName + suffix);
        }

        private static byte[] SerializeXml(XDocument document)
        {
            using (var output = new MemoryStream())
            {
                var settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    OmitXmlDeclaration = document.Declaration == null
                };

                using (var writer = XmlWriter.Create(output, settings))
                {
                    document.Save(writer);
                }

                return output.ToArray();
            }
        }

        private static void AppendImageInfo(StringBuilder builder, ImageInfo image)
        {
            builder.AppendLine("- path: " + image.Path);
            builder.AppendLine("- type: " + image.ImageType);
            builder.AppendLine("- pixels: " + image.PixelSize);
            builder.AppendLine("- bytes: " + image.ByteSize.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- sha256: " + image.Sha256);
        }

        private static string FormatInt(int value)
        {
            return value >= 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string EscapeMarkdownTable(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private sealed class TargetSelector
        {
            public string Kind { get; private set; }

            public int Index { get; private set; }

            public string ImageReference { get; private set; }

            public static TargetSelector Parse(string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    throw new ArgumentException("target selector is required");
                }

                if (raw.StartsWith("control:gso:", StringComparison.OrdinalIgnoreCase))
                {
                    return new TargetSelector
                    {
                        Kind = "control:gso",
                        Index = ParseNonNegative(raw.Substring("control:gso:".Length), "control:gso target index")
                    };
                }

                if (raw.StartsWith("picture:", StringComparison.OrdinalIgnoreCase))
                {
                    return new TargetSelector
                    {
                        Kind = "picture",
                        Index = ParseNonNegative(raw.Substring("picture:".Length), "picture target index")
                    };
                }

                if (raw.StartsWith("image:", StringComparison.OrdinalIgnoreCase))
                {
                    var imageReference = raw.Substring("image:".Length);
                    if (string.IsNullOrWhiteSpace(imageReference))
                    {
                        throw new ArgumentException("image target selector requires a binaryItemIDRef value");
                    }

                    return new TargetSelector
                    {
                        Kind = "image",
                        Index = -1,
                        ImageReference = imageReference
                    };
                }

                throw new ArgumentException("unsupported target selector. Use control:gso:<index>, picture:<index>, or image:<binaryItemIDRef>.");
            }

            public bool Matches(PictureMatch picture)
            {
                if (string.Equals(Kind, "control:gso", StringComparison.OrdinalIgnoreCase))
                {
                    return picture.PackageGsoIndex == Index;
                }

                if (string.Equals(Kind, "picture", StringComparison.OrdinalIgnoreCase))
                {
                    return picture.PictureIndex == Index;
                }

                return string.Equals(picture.ImageReference, ImageReference, StringComparison.OrdinalIgnoreCase);
            }

            public bool Matches(PictureInventoryItem picture)
            {
                if (string.Equals(Kind, "control:gso", StringComparison.OrdinalIgnoreCase))
                {
                    return picture.GsoTypeIndex == Index;
                }

                if (string.Equals(Kind, "picture", StringComparison.OrdinalIgnoreCase))
                {
                    return picture.PictureIndex == Index;
                }

                return string.Equals(picture.ImageReference, ImageReference, StringComparison.OrdinalIgnoreCase);
            }

            private static int ParseNonNegative(string raw, string name)
            {
                int value;
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 0)
                {
                    throw new ArgumentException(name + " must be a zero or positive integer");
                }

                return value;
            }
        }

        private sealed class PictureMatch
        {
            public string PartPath { get; set; }

            public XElement Picture { get; set; }

            public XElement ImageElement { get; set; }

            public int PictureIndex { get; set; }

            public int PackageGsoIndex { get; set; }

            public string ImageReference { get; set; }

            public string BinDataPath { get; set; }

            public PictureProperties Properties { get; set; }
        }

        internal sealed class PictureProperties
        {
            public static readonly PictureProperties Empty = new PictureProperties();

            public string ShapeId { get; set; }

            public string ZOrder { get; set; }

            public string Size { get; set; }

            public string Position { get; set; }

            public string OriginalSize { get; set; }

            public string CurrentSize { get; set; }

            public string ImageClip { get; set; }

            public string OutMargin { get; set; }

            public string TextWrap { get; set; }

            public string TextFlow { get; set; }

            public string TreatAsChar { get; set; }

            public static PictureProperties FromElement(XElement element)
            {
                return new PictureProperties
                {
                    ShapeId = GetAttribute(element, "id"),
                    ZOrder = GetAttribute(element, "zOrder"),
                    Size = FormatAttributes(FirstDescendant(element, "sz"), "width", "height", "widthRelTo", "heightRelTo", "protect"),
                    Position = FormatAttributes(FirstDescendant(element, "pos"), "treatAsChar", "vertRelTo", "horzRelTo", "vertAlign", "horzAlign", "vertOffset", "horzOffset", "flowWithText", "allowOverlap", "holdAnchorAndSO", "affectLSpacing"),
                    OriginalSize = FormatAttributes(FirstDescendant(element, "orgSz"), "width", "height"),
                    CurrentSize = FormatAttributes(FirstDescendant(element, "curSz"), "width", "height"),
                    ImageClip = FormatAttributes(FirstDescendant(element, "imgClip"), "left", "right", "top", "bottom"),
                    OutMargin = FormatAttributes(FirstDescendant(element, "outMargin"), "left", "right", "top", "bottom"),
                    TextWrap = GetAttribute(element, "textWrap"),
                    TextFlow = GetAttribute(element, "textFlow"),
                    TreatAsChar = GetAttribute(FirstDescendant(element, "pos"), "treatAsChar")
                };
            }

            public static PictureProperties FromInventory(PictureInventoryItem item)
            {
                if (item == null)
                {
                    return Empty;
                }

                return new PictureProperties
                {
                    ShapeId = item.ShapeId,
                    ZOrder = item.ZOrder,
                    Size = item.Size,
                    Position = item.Position,
                    OriginalSize = item.OriginalSize,
                    CurrentSize = item.CurrentSize,
                    ImageClip = item.ImageClip,
                    OutMargin = item.OutMargin,
                    TextWrap = item.TextWrap,
                    TextFlow = item.TextFlow,
                    TreatAsChar = item.TreatAsChar
                };
            }

            public IList<PropertyComparison> CompareTo(PictureProperties after)
            {
                after = after ?? Empty;
                return new List<PropertyComparison>
                {
                    Compare("shape id", ShapeId, after.ShapeId),
                    Compare("zOrder", ZOrder, after.ZOrder),
                    Compare("hp:sz", Size, after.Size),
                    Compare("hp:pos", Position, after.Position),
                    Compare("orgSz", OriginalSize, after.OriginalSize),
                    Compare("curSz", CurrentSize, after.CurrentSize),
                    Compare("imgClip", ImageClip, after.ImageClip),
                    Compare("outMargin", OutMargin, after.OutMargin),
                    Compare("textWrap", TextWrap, after.TextWrap),
                    Compare("textFlow", TextFlow, after.TextFlow),
                    Compare("treatAsChar", TreatAsChar, after.TreatAsChar)
                };
            }

            private static PropertyComparison Compare(string name, string before, string after)
            {
                return new PropertyComparison
                {
                    Name = name,
                    Before = before ?? string.Empty,
                    After = after ?? string.Empty,
                    Preserved = string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal)
                };
            }
        }
    }

    internal sealed class ReplaceImageControlOptions
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Target { get; set; }

        public string ImagePath { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class ImageReplacementResult
    {
        public ImageReplacementResult()
        {
            Verdict = "failed";
            BeforeImage = ImageInfo.Empty;
            AfterImage = ImageInfo.Empty;
            SourceImage = ImageInfo.Empty;
            BeforeProperties = HwpxImageControlReplacer.PictureProperties.Empty;
            AfterProperties = HwpxImageControlReplacer.PictureProperties.Empty;
            PropertyComparisons = new List<PropertyComparison>();
            TargetPictureIndex = -1;
            TargetPackageGsoIndex = -1;
        }

        public bool Success { get; set; }

        public string Verdict { get; set; }

        public string FailureReason { get; set; }

        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Target { get; set; }

        public string TargetPart { get; set; }

        public int TargetPictureIndex { get; set; }

        public int TargetPackageGsoIndex { get; set; }

        public string ImageReference { get; set; }

        public ImageInfo SourceImage { get; set; }

        public ImageInfo BeforeImage { get; set; }

        public ImageInfo AfterImage { get; set; }

        public string AfterBinDataPath { get; set; }

        public bool OldBinDataRetained { get; set; }

        public int PictureCountBefore { get; set; }

        public int PictureCountAfter { get; set; }

        public int TableCountBefore { get; set; }

        public int TableCountAfter { get; set; }

        public bool HashVerified { get; set; }

        public bool PropertiesPreserved { get; set; }

        public bool? LayoutPassed { get; set; }

        public string LayoutReportPath { get; set; }

        public string LayoutSkippedReason { get; set; }

        public HwpxImageControlReplacer.PictureProperties BeforeProperties { get; set; }

        public HwpxImageControlReplacer.PictureProperties AfterProperties { get; set; }

        public IList<PropertyComparison> PropertyComparisons { get; set; }

        public ImageReplacementResult Fail(string reason)
        {
            Success = false;
            Verdict = "failed";
            FailureReason = reason ?? string.Empty;
            return this;
        }
    }

    internal sealed class ImageInfo
    {
        public static readonly ImageInfo Empty = new ImageInfo();

        public string Path { get; set; }

        public string ImageType { get; set; }

        public string PixelSize { get; set; }

        public int ByteSize { get; set; }

        public string Sha256 { get; set; }
    }

    internal sealed class PropertyComparison
    {
        public string Name { get; set; }

        public string Before { get; set; }

        public string After { get; set; }

        public bool Preserved { get; set; }
    }
}
