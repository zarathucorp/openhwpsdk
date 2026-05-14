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
    internal static class HwpxPictureInspector
    {
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

        public static PictureInventorySummary Inspect(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("Input path is required.", nameof(inputPath));
            }

            var fullPath = Path.GetFullPath(inputPath);
            var summary = new PictureInventorySummary(fullPath);
            foreach (var file in ResolveHwpxFiles(fullPath))
            {
                var result = InspectFile(file);
                summary.Files.Add(result);
            }

            return summary;
        }

        public static void WriteMarkdown(PictureInventorySummary summary, string reportPath)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("# HWPX Picture Inventory");
            builder.AppendLine();
            builder.AppendLine("- input: " + summary.InputPath);
            builder.AppendLine("- files: " + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- package errors: " + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- XML parse errors: " + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- pictures: " + summary.PictureCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- matched BinData: " + summary.MatchedBinDataCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- missing BinData: " + summary.MissingBinDataCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();

            builder.AppendLine("## Pictures");
            builder.AppendLine();
            builder.AppendLine("> `package gso index` is a package-order inspection aid, not an authoritative HWP COM control identity. Use `list-controls` when editor control identity is required.");
            builder.AppendLine();
            builder.AppendLine("| file | part | picture | package gso index | shape id | zOrder | image ref | BinData | type | pixels | bytes | sha256 | size | position | orgSz | curSz | imgClip | outMargin | wrap | textFlow | treatAsChar |");
            builder.AppendLine("| --- | --- | ---: | ---: | --- | ---: | --- | --- | --- | --- | ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var picture in summary.Files
                .SelectMany(file => file.Pictures)
                .OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PartPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PictureIndex))
            {
                builder.Append("| ");
                builder.Append(EscapeMarkdownTable(Path.GetFileName(picture.FilePath)));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.PartPath));
                builder.Append(" | ");
                builder.Append(picture.PictureIndex.ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(picture.GsoTypeIndex >= 0 ? picture.GsoTypeIndex.ToString(CultureInfo.InvariantCulture) : string.Empty);
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.ShapeId));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.ZOrder));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.ImageReference));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.BinDataPath));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.ImageType));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.PixelSize));
                builder.Append(" | ");
                builder.Append(picture.ByteSize >= 0 ? picture.ByteSize.ToString(CultureInfo.InvariantCulture) : string.Empty);
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.Sha256));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.Size));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.Position));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.OriginalSize));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.CurrentSize));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.ImageClip));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.OutMargin));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.TextWrap));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.TextFlow));
                builder.Append(" | ");
                builder.Append(EscapeMarkdownTable(picture.TreatAsChar));
                builder.AppendLine(" |");
            }

            var errors = summary.Files.Where(item => item.PackageError.Length > 0 || item.XmlErrors.Count > 0).ToList();
            if (errors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("## Errors");
                foreach (var file in errors)
                {
                    if (file.PackageError.Length > 0)
                    {
                        builder.AppendLine("- " + file.Path + ": " + file.PackageError);
                    }

                    foreach (var error in file.XmlErrors)
                    {
                        builder.AppendLine("- " + file.Path + " / " + error);
                    }
                }
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, builder.ToString(), new UTF8Encoding(true));
        }

        private static PictureFileResult InspectFile(string path)
        {
            var result = new PictureFileResult(path);
            IDictionary<string, byte[]> entries;
            try
            {
                entries = SimpleZipArchive.ReadAll(path);
            }
            catch (Exception ex)
            {
                result.PackageError = ex.GetType().Name + ": " + ex.Message;
                return result;
            }

            var binData = BuildBinDataIndex(entries);
            var referenceMap = BuildImageReferenceMap(entries, result);
            var gsoTypeIndex = -1;
            var pictureIndex = 0;
            foreach (var partPath in ResolvePicturePartPaths(entries.Keys))
            {
                XDocument document;
                try
                {
                    document = XDocument.Parse(Encoding.UTF8.GetString(entries[partPath]), LoadOptions.PreserveWhitespace);
                }
                catch (XmlException ex)
                {
                    result.XmlErrors.Add(partPath + ": " + ex.Message);
                    continue;
                }

                InspectPart(result, partPath, document, binData, referenceMap, ref gsoTypeIndex, ref pictureIndex);
            }

            return result;
        }

        private static void InspectPart(
            PictureFileResult result,
            string partPath,
            XDocument document,
            IDictionary<string, BinDataInfo> binData,
            IDictionary<string, string> referenceMap,
            ref int gsoTypeIndex,
            ref int pictureIndex)
        {
            foreach (var element in document.Descendants())
            {
                if (IsGraphicalObject(element))
                {
                    gsoTypeIndex++;
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

                var resolvedBinDataPath = ResolveBinDataPath(imageReference, referenceMap, binData);
                BinDataInfo info = null;
                if (!string.IsNullOrWhiteSpace(resolvedBinDataPath))
                {
                    binData.TryGetValue(resolvedBinDataPath, out info);
                }

                result.Pictures.Add(new PictureInventoryItem
                {
                    FilePath = result.Path,
                    PartPath = partPath,
                    PictureIndex = pictureIndex,
                    GsoTypeIndex = gsoTypeIndex,
                    ShapeId = GetAttribute(element, "id"),
                    ZOrder = GetAttribute(element, "zOrder"),
                    ImageReference = imageReference,
                    BinDataPath = resolvedBinDataPath,
                    ImageType = info == null ? string.Empty : info.ImageType,
                    PixelSize = info == null ? string.Empty : FormatSize(info.Width, info.Height),
                    ByteSize = info == null ? -1 : info.ByteSize,
                    Sha256 = info == null ? string.Empty : info.Sha256,
                    Size = FormatAttributes(FirstDescendant(element, "sz"), "width", "height", "widthRelTo", "heightRelTo", "protect"),
                    Position = FormatAttributes(FirstDescendant(element, "pos"), "treatAsChar", "vertRelTo", "horzRelTo", "vertAlign", "horzAlign", "vertOffset", "horzOffset", "flowWithText", "allowOverlap", "holdAnchorAndSO", "affectLSpacing"),
                    OriginalSize = FormatAttributes(FirstDescendant(element, "orgSz"), "width", "height"),
                    CurrentSize = FormatAttributes(FirstDescendant(element, "curSz"), "width", "height"),
                    ImageClip = FormatAttributes(FirstDescendant(element, "imgClip"), "left", "right", "top", "bottom"),
                    OutMargin = FormatAttributes(FirstDescendant(element, "outMargin"), "left", "right", "top", "bottom"),
                    TextWrap = GetAttribute(element, "textWrap"),
                    TextFlow = GetAttribute(element, "textFlow"),
                    TreatAsChar = GetAttribute(FirstDescendant(element, "pos"), "treatAsChar")
                });
                pictureIndex++;
            }
        }

        private static IDictionary<string, BinDataInfo> BuildBinDataIndex(IDictionary<string, byte[]> entries)
        {
            var result = new Dictionary<string, BinDataInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.Where(item => NormalizePackagePath(item.Key).StartsWith("BinData/", StringComparison.OrdinalIgnoreCase)))
            {
                var data = entry.Value ?? new byte[0];
                var info = ReadBinDataInfo(NormalizePackagePath(entry.Key), data);
                result[info.Path] = info;
            }

            return result;
        }

        private static BinDataInfo ReadBinDataInfo(string path, byte[] data)
        {
            var info = new BinDataInfo
            {
                Path = NormalizePackagePath(path),
                ByteSize = data == null ? 0 : data.Length,
                Sha256 = ComputeSha256(data ?? new byte[0]),
                ImageType = ImageTypeFromExtension(path),
                Width = -1,
                Height = -1
            };

            try
            {
                using (var stream = new MemoryStream(data ?? new byte[0]))
                using (var image = Image.FromStream(stream, false, false))
                {
                    info.Width = image.Width;
                    info.Height = image.Height;
                }
            }
            catch
            {
                info.ImageType = string.Empty;
            }

            return info;
        }

        private static IDictionary<string, string> BuildImageReferenceMap(IDictionary<string, byte[]> entries, PictureFileResult result)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var xmlPath in new[] { "Contents/header.xml", "header.xml", "Contents/content.hpf", "content.hpf" })
            {
                byte[] data;
                if (!entries.TryGetValue(xmlPath, out data))
                {
                    continue;
                }

                XDocument document;
                try
                {
                    document = XDocument.Parse(Encoding.UTF8.GetString(data), LoadOptions.PreserveWhitespace);
                }
                catch (XmlException ex)
                {
                    result.XmlErrors.Add(xmlPath + ": " + ex.Message);
                    continue;
                }

                foreach (var element in document.Descendants())
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

                    if (string.IsNullOrWhiteSpace(href))
                    {
                        href = GetAttribute(element, "binaryItemIDRef");
                    }

                    if (string.IsNullOrWhiteSpace(href))
                    {
                        continue;
                    }

                    map[id] = ResolvePackageHref(xmlPath, href);
                }
            }

            return map;
        }

        private static string ResolveBinDataPath(string imageReference, IDictionary<string, string> referenceMap, IDictionary<string, BinDataInfo> binData)
        {
            if (string.IsNullOrWhiteSpace(imageReference))
            {
                return string.Empty;
            }

            string mapped;
            if (referenceMap.TryGetValue(imageReference, out mapped))
            {
                var normalized = NormalizePackagePath(mapped);
                if (binData.ContainsKey(normalized))
                {
                    return normalized;
                }

                var mappedFileName = Path.GetFileName(normalized);
                var match = binData.Keys.FirstOrDefault(item => string.Equals(Path.GetFileName(item), mappedFileName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return match;
                }
            }

            if (imageReference.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase))
            {
                var normalized = NormalizePackagePath(imageReference);
                if (binData.ContainsKey(normalized))
                {
                    return normalized;
                }
            }

            return binData.Keys.FirstOrDefault(item => string.Equals(Path.GetFileNameWithoutExtension(item), imageReference, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
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

        private static IEnumerable<string> ResolveHwpxFiles(string fullPath)
        {
            if (File.Exists(fullPath))
            {
                yield return fullPath;
                yield break;
            }

            if (!Directory.Exists(fullPath))
            {
                throw new FileNotFoundException("Input file or directory was not found.", fullPath);
            }

            foreach (var file in Directory.GetFiles(fullPath, "*.hwpx", SearchOption.AllDirectories).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        private static XElement FirstDescendant(XElement element, string localName)
        {
            return element == null
                ? null
                : element.Descendants().FirstOrDefault(item => string.Equals(item.Name.LocalName, localName, StringComparison.Ordinal));
        }

        private static bool IsGraphicalObject(XElement element)
        {
            return element != null && GraphicalObjectNames.Any(name => string.Equals(element.Name.LocalName, name, StringComparison.Ordinal));
        }

        private static bool IsSectionPart(string path)
        {
            var fileName = Path.GetFileName(path);
            return path.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                   fileName.StartsWith("section", StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
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

            if (xmlPath.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                (normalizedHref.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase) ||
                 normalizedHref.StartsWith("bindata/", StringComparison.OrdinalIgnoreCase)))
            {
                return normalizedHref;
            }

            if (xmlPath.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                normalizedHref.IndexOf('/') < 0)
            {
                return "BinData/" + normalizedHref;
            }

            return normalizedHref;
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

        private static string FormatSize(int width, int height)
        {
            return width > 0 && height > 0
                ? width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string ImageTypeFromExtension(string path)
        {
            return (Path.GetExtension(path) ?? string.Empty).TrimStart('.').ToLowerInvariant();
        }

        private static string ComputeSha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(data ?? new byte[0])).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string NormalizePackagePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }

        private static string EscapeMarkdownTable(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private sealed class BinDataInfo
        {
            public string Path { get; set; }

            public string ImageType { get; set; }

            public int Width { get; set; }

            public int Height { get; set; }

            public int ByteSize { get; set; }

            public string Sha256 { get; set; }
        }
    }

    internal sealed class PictureInventorySummary
    {
        public PictureInventorySummary(string inputPath)
        {
            InputPath = inputPath ?? string.Empty;
            Files = new List<PictureFileResult>();
        }

        public string InputPath { get; private set; }

        public IList<PictureFileResult> Files { get; private set; }

        public int PackageErrors
        {
            get { return Files.Count(item => item.PackageError.Length > 0); }
        }

        public int XmlParseErrors
        {
            get { return Files.Sum(item => item.XmlErrors.Count); }
        }

        public int PictureCount
        {
            get { return Files.Sum(item => item.Pictures.Count); }
        }

        public int MatchedBinDataCount
        {
            get { return Files.Sum(item => item.Pictures.Count(picture => !string.IsNullOrWhiteSpace(picture.BinDataPath) && !string.IsNullOrWhiteSpace(picture.Sha256))); }
        }

        public int MissingBinDataCount
        {
            get { return Files.Sum(item => item.Pictures.Count(picture => string.IsNullOrWhiteSpace(picture.BinDataPath) || string.IsNullOrWhiteSpace(picture.Sha256))); }
        }
    }

    internal sealed class PictureFileResult
    {
        public PictureFileResult(string path)
        {
            Path = path ?? string.Empty;
            PackageError = string.Empty;
            XmlErrors = new List<string>();
            Pictures = new List<PictureInventoryItem>();
        }

        public string Path { get; private set; }

        public string PackageError { get; set; }

        public IList<string> XmlErrors { get; private set; }

        public IList<PictureInventoryItem> Pictures { get; private set; }
    }

    internal sealed class PictureInventoryItem
    {
        public string FilePath { get; set; }

        public string PartPath { get; set; }

        public int PictureIndex { get; set; }

        public int GsoTypeIndex { get; set; }

        public string ShapeId { get; set; }

        public string ZOrder { get; set; }

        public string ImageReference { get; set; }

        public string BinDataPath { get; set; }

        public string ImageType { get; set; }

        public string PixelSize { get; set; }

        public int ByteSize { get; set; }

        public string Sha256 { get; set; }

        public string Size { get; set; }

        public string Position { get; set; }

        public string OriginalSize { get; set; }

        public string CurrentSize { get; set; }

        public string ImageClip { get; set; }

        public string OutMargin { get; set; }

        public string TextWrap { get; set; }

        public string TextFlow { get; set; }

        public string TreatAsChar { get; set; }
    }
}
