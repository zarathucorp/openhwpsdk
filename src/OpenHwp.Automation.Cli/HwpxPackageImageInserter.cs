using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxPackageImageInserter
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";
        private static readonly XNamespace Hc = "http://www.hancom.co.kr/hwpml/2011/core";
        private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf/";

        private const double HwpxUnitsPerInch = 7200.0;
        private const double FallbackImageDpi = 96.0;
        private const int FallbackBodyWidth = 48190;
        private const int FallbackBodyHeight = 78518;

        public static int InsertImagesAtAnchors(string hwpxPath, IEnumerable<SubmissionTemplateFiller.ImageWriteOperation> imageWrites)
        {
            var writes = (imageWrites ?? Enumerable.Empty<SubmissionTemplateFiller.ImageWriteOperation>())
                .Where(item => !string.Equals(item.Status, "applied", StringComparison.OrdinalIgnoreCase))
                .Where(item => !string.IsNullOrWhiteSpace(item.AnchorText))
                .ToList();
            if (writes.Count == 0)
            {
                return 0;
            }

            var entries = SimpleZipArchive.ReadAll(hwpxPath);
            var sections = ReadSectionDocuments(entries).ToList();
            var packageKey = entries.ContainsKey("Contents/content.hpf") ? "Contents/content.hpf" : (entries.ContainsKey("content.hpf") ? "content.hpf" : null);
            if (sections.Count == 0 || packageKey == null)
            {
                foreach (var write in writes)
                {
                    write.Status = "failed";
                    write.Note = "package fallback could not find section*.xml or content.hpf";
                }

                return 0;
            }

            var package = XDocument.Parse(Encoding.UTF8.GetString(entries[packageKey]), LoadOptions.PreserveWhitespace);
            var nextImageIndex = NextImageIndex(entries, package);
            var nextShapeId = NextShapeId(sections.Select(item => item.Document));
            var nextInstanceId = NextInstanceId(sections.Select(item => item.Document));
            var nextZOrder = NextZOrder(sections.Select(item => item.Document));
            var touchedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inserted = 0;

            foreach (var write in writes)
            {
                if (string.IsNullOrWhiteSpace(write.ResolvedPath) || !File.Exists(write.ResolvedPath))
                {
                    write.Status = "failed";
                    write.Note = "image file was not found; package fallback skipped";
                    continue;
                }

                var targetSection = FindSectionWithAnchor(sections, write.AnchorText, out var textNode);
                if (textNode == null)
                {
                    write.Status = "failed";
                    write.Note = "anchor was not found for package fallback";
                    continue;
                }

                ImageSize imageSize;
                try
                {
                    imageSize = ReadImageSize(write.ResolvedPath);
                }
                catch (Exception ex)
                {
                    write.Status = "failed";
                    write.Note = "package fallback could not read image: " + ex.GetType().Name + ": " + ex.Message;
                    continue;
                }

                var extension = NormalizeImageExtension(Path.GetExtension(write.ResolvedPath));
                var imageId = "image" + nextImageIndex.ToString(CultureInfo.InvariantCulture);
                var imageEntryPath = "BinData/" + imageId + extension;
                entries[imageEntryPath] = File.ReadAllBytes(write.ResolvedPath);
                AddPackageManifestItem(package, imageId, imageEntryPath, MediaTypeForExtension(extension));

                var displaySize = FitDisplaySize(imageSize, GetBodyArea(targetSection.Document));
                var picture = CreatePictureElement(
                    imageId,
                    write.ResolvedPath,
                    imageSize,
                    displaySize,
                    nextShapeId++,
                    nextInstanceId++,
                    nextZOrder++);
                InsertPictureAfterAnchor(textNode, write.AnchorText, picture, true);
                touchedSections.Add(targetSection.Path);
                nextImageIndex++;
                inserted++;
                write.Status = "applied";
                write.Note = string.Format(
                    CultureInfo.InvariantCulture,
                    "inserted by HWPX package fallback; display={0}x{1}",
                    displaySize.Width,
                    displaySize.Height);
            }

            if (inserted > 0)
            {
                foreach (var section in sections)
                {
                    if (touchedSections.Contains(section.Path))
                    {
                        entries[section.Path] = SerializeXml(section.Document);
                    }
                }

                entries[packageKey] = SerializeXml(package);
                SimpleZipArchive.WriteAllPreservingTemplate(hwpxPath, hwpxPath, entries);
            }

            return inserted;
        }

        private static SectionDocument FindSectionWithAnchor(IEnumerable<SectionDocument> sections, string anchorText, out XElement textNode)
        {
            foreach (var section in sections ?? Enumerable.Empty<SectionDocument>())
            {
                textNode = section.Document
                    .Descendants(Hp + "t")
                    .FirstOrDefault(item => (item.Value ?? string.Empty).Contains(anchorText));
                if (textNode != null)
                {
                    return section;
                }
            }

            textNode = null;
            return null;
        }

        public static bool TryInsertImageAtParagraph(
            IDictionary<string, byte[]> entries,
            IDictionary<string, XDocument> xmlDocuments,
            ISet<string> touchedParts,
            string partPath,
            XElement paragraph,
            string anchorText,
            string imagePath,
            int requestedWidth,
            int requestedHeight,
            bool removeAnchorText,
            out string note)
        {
            if (paragraph == null)
            {
                note = "paragraph was not found";
                return false;
            }

            XElement textNode = null;
            if (!string.IsNullOrWhiteSpace(anchorText))
            {
                textNode = HwpxTableModel.DirectTextNodesOfParagraph(paragraph)
                    .FirstOrDefault(item => (item.Value ?? string.Empty).Contains(anchorText));
            }

            if (textNode == null)
            {
                if (removeAnchorText &&
                    !string.IsNullOrWhiteSpace(anchorText) &&
                    string.Equals(NormalizeText(HwpxTableModel.TextOfDirectParagraph(paragraph)), NormalizeText(anchorText), StringComparison.Ordinal))
                {
                    ClearDirectParagraphText(paragraph);
                }

                textNode = EnsureParagraphInsertionTextNode(paragraph);
                removeAnchorText = false;
            }

            XElement picture;
            DisplaySize displaySize;
            if (!TryCreatePackagePicture(entries, xmlDocuments, touchedParts, paragraph, imagePath, requestedWidth, requestedHeight, out picture, out displaySize, out note))
            {
                return false;
            }

            InsertPictureAfterAnchor(textNode, anchorText, picture, removeAnchorText);
            if (!string.IsNullOrWhiteSpace(partPath))
            {
                touchedParts.Add(partPath);
            }

            note = string.Format(
                CultureInfo.InvariantCulture,
                "package image; display={0}x{1}",
                displaySize.Width,
                displaySize.Height);
            return true;
        }

        public static bool TryInsertImageInParagraph(
            IDictionary<string, byte[]> entries,
            IDictionary<string, XDocument> xmlDocuments,
            ISet<string> touchedParts,
            string partPath,
            XElement paragraph,
            string imagePath,
            int requestedWidth,
            int requestedHeight,
            out string note)
        {
            if (paragraph == null)
            {
                note = "paragraph was not found";
                return false;
            }

            XElement picture;
            DisplaySize displaySize;
            if (!TryCreatePackagePicture(entries, xmlDocuments, touchedParts, paragraph, imagePath, requestedWidth, requestedHeight, out picture, out displaySize, out note))
            {
                return false;
            }

            var textNode = EnsureParagraphInsertionTextNode(paragraph);
            InsertPictureAfterTextNode(textNode, picture);
            if (!string.IsNullOrWhiteSpace(partPath))
            {
                touchedParts.Add(partPath);
            }

            note = string.Format(
                CultureInfo.InvariantCulture,
                "package image; display={0}x{1}",
                displaySize.Width,
                displaySize.Height);
            return true;
        }

        private static bool TryCreatePackagePicture(
            IDictionary<string, byte[]> entries,
            IDictionary<string, XDocument> xmlDocuments,
            ISet<string> touchedParts,
            XElement paragraph,
            string imagePath,
            int requestedWidth,
            int requestedHeight,
            out XElement picture,
            out DisplaySize displaySize,
            out string note)
        {
            picture = null;
            displaySize = new DisplaySize(0, 0);
            note = string.Empty;

            if (entries == null)
            {
                note = "package entries are missing";
                return false;
            }

            if (xmlDocuments == null)
            {
                note = "package XML documents are missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                note = "image file was not found";
                return false;
            }

            var packageKey = FindContentPackagePath(entries, xmlDocuments);
            if (string.IsNullOrWhiteSpace(packageKey))
            {
                note = "content.hpf was not found";
                return false;
            }

            XDocument package;
            if (!xmlDocuments.TryGetValue(packageKey, out package))
            {
                note = "content package XML was not parsed: " + packageKey;
                return false;
            }

            ImageSize imageSize;
            try
            {
                imageSize = ReadImageSize(imagePath);
            }
            catch (Exception ex)
            {
                note = "could not read image: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            var section = paragraph == null ? null : paragraph.Document;
            var extension = NormalizeImageExtension(Path.GetExtension(imagePath));
            var imageId = "image" + NextImageIndex(entries, package).ToString(CultureInfo.InvariantCulture);
            var imageEntryPath = "BinData/" + imageId + extension;
            entries[imageEntryPath] = File.ReadAllBytes(imagePath);
            AddPackageManifestItem(package, imageId, imageEntryPath, MediaTypeForExtension(extension));
            if (touchedParts != null)
            {
                touchedParts.Add(packageKey);
            }

            displaySize = ResolveDisplaySize(imageSize, section, requestedWidth, requestedHeight);
            picture = CreatePictureElement(
                imageId,
                imagePath,
                imageSize,
                displaySize,
                NextShapeId(xmlDocuments.Values),
                NextInstanceId(xmlDocuments.Values),
                NextZOrder(xmlDocuments.Values));
            return true;
        }

        private static void InsertPictureAfterAnchor(XElement textNode, string anchorText, XElement picture, bool removeAnchorText)
        {
            var paragraph = textNode.Ancestors(Hp + "p").FirstOrDefault();
            if (paragraph != null)
            {
                paragraph.Elements(Hp + "linesegarray").Remove();
            }

            var current = textNode.Value ?? string.Empty;
            if (removeAnchorText && !string.IsNullOrEmpty(anchorText))
            {
                textNode.Value = current.Replace(anchorText, string.Empty);
            }

            if (textNode.Value.Length == 0)
            {
                textNode.Value = " ";
            }

            InsertPictureAfterTextNode(textNode, picture);
        }

        private static void InsertPictureAfterTextNode(XElement textNode, XElement picture)
        {
            var paragraph = textNode.Ancestors(Hp + "p").FirstOrDefault();
            if (paragraph != null)
            {
                paragraph.Elements(Hp + "linesegarray").Remove();
            }

            if ((textNode.Value ?? string.Empty).Length == 0)
            {
                textNode.Value = " ";
            }

            textNode.AddAfterSelf(picture);
            picture.AddAfterSelf(new XElement(Hp + "t"));
        }

        private static XElement EnsureParagraphInsertionTextNode(XElement paragraph)
        {
            var textNode = HwpxTableModel.DirectTextNodesOfParagraph(paragraph).LastOrDefault();
            if (textNode != null)
            {
                return textNode;
            }

            var run = paragraph.Elements(Hp + "run").FirstOrDefault();
            if (run == null)
            {
                run = new XElement(Hp + "run");
                var lineSegArray = paragraph.Element(Hp + "linesegarray");
                if (lineSegArray == null)
                {
                    paragraph.Add(run);
                }
                else
                {
                    lineSegArray.AddBeforeSelf(run);
                }
            }

            textNode = new XElement(Hp + "t", " ");
            run.Add(textNode);
            return textNode;
        }

        private static void ClearDirectParagraphText(XElement paragraph)
        {
            foreach (var textNode in HwpxTableModel.DirectTextNodesOfParagraph(paragraph))
            {
                textNode.Value = string.Empty;
            }
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var previousWasWhiteSpace = false;
            foreach (var character in value.Trim())
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasWhiteSpace)
                    {
                        builder.Append(' ');
                    }

                    previousWasWhiteSpace = true;
                }
                else
                {
                    builder.Append(character);
                    previousWasWhiteSpace = false;
                }
            }

            return builder.ToString();
        }

        private static XElement CreatePictureElement(
            string imageId,
            string sourcePath,
            ImageSize imageSize,
            DisplaySize displaySize,
            long shapeId,
            long instanceId,
            int zOrder)
        {
            var sourceName = Path.GetFileName(sourcePath);
            var dimWidth = NaturalHwpxWidth(imageSize);
            var dimHeight = NaturalHwpxHeight(imageSize);
            var width = Math.Max(1, displaySize.Width);
            var height = Math.Max(1, displaySize.Height);

            return new XElement(Hp + "pic",
                new XAttribute("id", shapeId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("zOrder", zOrder.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("numberingType", "PICTURE"),
                new XAttribute("textWrap", "TOP_AND_BOTTOM"),
                new XAttribute("textFlow", "BOTH_SIDES"),
                new XAttribute("lock", "0"),
                new XAttribute("dropcapstyle", "None"),
                new XAttribute("href", string.Empty),
                new XAttribute("groupLevel", "0"),
                new XAttribute("instid", instanceId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("reverse", "0"),
                new XElement(Hp + "offset", new XAttribute("x", "0"), new XAttribute("y", "0")),
                new XElement(Hp + "orgSz", new XAttribute("width", width.ToString(CultureInfo.InvariantCulture)), new XAttribute("height", height.ToString(CultureInfo.InvariantCulture))),
                new XElement(Hp + "curSz", new XAttribute("width", "0"), new XAttribute("height", "0")),
                new XElement(Hp + "flip", new XAttribute("horizontal", "0"), new XAttribute("vertical", "0")),
                new XElement(Hp + "rotationInfo",
                    new XAttribute("angle", "0"),
                    new XAttribute("centerX", (width / 2).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("centerY", (height / 2).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("rotateimage", "1")),
                CreateRenderingInfo(),
                new XElement(Hc + "img",
                    new XAttribute("binaryItemIDRef", imageId),
                    new XAttribute("bright", "0"),
                    new XAttribute("contrast", "0"),
                    new XAttribute("effect", "REAL_PIC"),
                    new XAttribute("alpha", "0")),
                new XElement(Hp + "imgRect",
                    new XElement(Hc + "pt0", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(Hc + "pt1", new XAttribute("x", width.ToString(CultureInfo.InvariantCulture)), new XAttribute("y", "0")),
                    new XElement(Hc + "pt2", new XAttribute("x", width.ToString(CultureInfo.InvariantCulture)), new XAttribute("y", height.ToString(CultureInfo.InvariantCulture))),
                    new XElement(Hc + "pt3", new XAttribute("x", "0"), new XAttribute("y", height.ToString(CultureInfo.InvariantCulture)))),
                new XElement(Hp + "imgClip",
                    new XAttribute("left", "0"),
                    new XAttribute("right", dimWidth.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("top", "0"),
                    new XAttribute("bottom", dimHeight.ToString(CultureInfo.InvariantCulture))),
                new XElement(Hp + "inMargin", new XAttribute("left", "0"), new XAttribute("right", "0"), new XAttribute("top", "0"), new XAttribute("bottom", "0")),
                new XElement(Hp + "imgDim", new XAttribute("dimwidth", dimWidth.ToString(CultureInfo.InvariantCulture)), new XAttribute("dimheight", dimHeight.ToString(CultureInfo.InvariantCulture))),
                new XElement(Hp + "effects"),
                new XElement(Hp + "sz",
                    new XAttribute("width", width.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("widthRelTo", "ABSOLUTE"),
                    new XAttribute("height", height.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("heightRelTo", "ABSOLUTE"),
                    new XAttribute("protect", "0")),
                new XElement(Hp + "pos",
                    new XAttribute("treatAsChar", "1"),
                    new XAttribute("affectLSpacing", "0"),
                    new XAttribute("flowWithText", "1"),
                    new XAttribute("allowOverlap", "0"),
                    new XAttribute("holdAnchorAndSO", "0"),
                    new XAttribute("vertRelTo", "PARA"),
                    new XAttribute("horzRelTo", "COLUMN"),
                    new XAttribute("vertAlign", "TOP"),
                    new XAttribute("horzAlign", "LEFT"),
                    new XAttribute("vertOffset", "0"),
                    new XAttribute("horzOffset", "0")),
                new XElement(Hp + "outMargin", new XAttribute("left", "0"), new XAttribute("right", "0"), new XAttribute("top", "0"), new XAttribute("bottom", "0")),
                new XElement(Hp + "shapeComment", string.Format(
                    CultureInfo.InvariantCulture,
                    "그림입니다.\n원본 그림의 이름: {0}\n원본 그림의 크기: 가로 {1}pixel, 세로 {2}pixel",
                    sourceName,
                    imageSize.Width,
                    imageSize.Height)));
        }

        private static XElement CreateRenderingInfo()
        {
            return new XElement(Hp + "renderingInfo",
                CreateIdentityMatrix("transMatrix"),
                CreateIdentityMatrix("scaMatrix"),
                CreateIdentityMatrix("rotMatrix"));
        }

        private static XElement CreateIdentityMatrix(string localName)
        {
            return new XElement(Hc + localName,
                new XAttribute("e1", "1"),
                new XAttribute("e2", "0"),
                new XAttribute("e3", "0"),
                new XAttribute("e4", "0"),
                new XAttribute("e5", "1"),
                new XAttribute("e6", "0"));
        }

        private static void AddPackageManifestItem(XDocument package, string id, string href, string mediaType)
        {
            var manifest = package.Descendants(Opf + "manifest").FirstOrDefault();
            if (manifest == null)
            {
                return;
            }

            manifest.Elements(Opf + "item")
                .Where(item => string.Equals(GetString(item, "id"), id, StringComparison.Ordinal))
                .Remove();
            manifest.Add(new XElement(Opf + "item",
                new XAttribute("id", id),
                new XAttribute("href", href),
                new XAttribute("media-type", mediaType),
                new XAttribute("isEmbeded", "1")));
        }

        private static int NextImageIndex(IDictionary<string, byte[]> entries, XDocument package)
        {
            var indexes = new List<int>();
            foreach (var name in entries.Keys)
            {
                var fileName = Path.GetFileNameWithoutExtension(name.Replace('\\', '/'));
                AddImageIndex(indexes, fileName);
            }

            foreach (var item in package.Descendants(Opf + "item"))
            {
                AddImageIndex(indexes, GetString(item, "id"));
            }

            return indexes.DefaultIfEmpty(0).Max() + 1;
        }

        private static void AddImageIndex(ICollection<int> indexes, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int parsed;
            if (int.TryParse(value.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                indexes.Add(parsed);
            }
        }

        private static long NextShapeId(IEnumerable<XDocument> documents)
        {
            return Pictures(documents)
                .Select(item => GetLong(item, "id", 1000000000))
                .DefaultIfEmpty(1000000000)
                .Max() + 1;
        }

        private static long NextInstanceId(IEnumerable<XDocument> documents)
        {
            return Pictures(documents)
                .Select(item => GetLong(item, "instid", 5000000))
                .DefaultIfEmpty(5000000)
                .Max() + 1;
        }

        private static int NextZOrder(IEnumerable<XDocument> documents)
        {
            return Pictures(documents)
                .Select(item => GetInt(item, "zOrder", 100))
                .DefaultIfEmpty(100)
                .Max() + 1;
        }

        private static IEnumerable<XElement> Pictures(IEnumerable<XDocument> documents)
        {
            foreach (var document in documents ?? Enumerable.Empty<XDocument>())
            {
                if (document == null)
                {
                    continue;
                }

                foreach (var picture in document.Descendants(Hp + "pic"))
                {
                    yield return picture;
                }
            }
        }

        private static IEnumerable<SectionDocument> ReadSectionDocuments(IDictionary<string, byte[]> entries)
        {
            foreach (var path in HwpxSectionPartResolver.GetBodySectionPartPaths(entries))
            {
                yield return new SectionDocument(
                    path,
                    XDocument.Parse(Encoding.UTF8.GetString(entries[path]), LoadOptions.PreserveWhitespace));
            }
        }

        private static ImageSize ReadImageSize(string path)
        {
            using (var image = Image.FromFile(path))
            {
                return new ImageSize(
                    image.Width,
                    image.Height,
                    NormalizeDpi(image.HorizontalResolution),
                    NormalizeDpi(image.VerticalResolution));
            }
        }

        private static DisplaySize FitDisplaySize(ImageSize imageSize, BodyArea bodyArea)
        {
            var naturalWidth = NaturalHwpxWidth(imageSize);
            var naturalHeight = NaturalHwpxHeight(imageSize);
            var maxWidth = Math.Max(1, bodyArea.Width);
            var maxHeight = Math.Max(1, bodyArea.Height);
            var scale = Math.Min(1.0, Math.Min(
                (double)maxWidth / naturalWidth,
                (double)maxHeight / naturalHeight));
            return new DisplaySize(
                Math.Max(1, (int)Math.Round(naturalWidth * scale)),
                Math.Max(1, (int)Math.Round(naturalHeight * scale)));
        }

        private static DisplaySize ResolveDisplaySize(ImageSize imageSize, XDocument section, int requestedWidth, int requestedHeight)
        {
            if (requestedWidth > 1000 && requestedHeight > 1000)
            {
                return new DisplaySize(requestedWidth, requestedHeight);
            }

            return FitDisplaySize(imageSize, GetBodyArea(section));
        }

        private static int NaturalHwpxWidth(ImageSize imageSize)
        {
            return PixelsToHwpxUnits(imageSize.Width, imageSize.HorizontalDpi);
        }

        private static int NaturalHwpxHeight(ImageSize imageSize)
        {
            return PixelsToHwpxUnits(imageSize.Height, imageSize.VerticalDpi);
        }

        private static int PixelsToHwpxUnits(int pixels, double dpi)
        {
            return Math.Max(1, (int)Math.Round(Math.Max(1, pixels) * HwpxUnitsPerInch / NormalizeDpi(dpi)));
        }

        private static double NormalizeDpi(double dpi)
        {
            return double.IsNaN(dpi) || double.IsInfinity(dpi) || dpi < 1.0
                ? FallbackImageDpi
                : dpi;
        }

        private static BodyArea GetBodyArea(XDocument section)
        {
            var page = section == null ? null : section.Descendants(Hp + "pagePr").FirstOrDefault();
            if (page == null)
            {
                return new BodyArea(FallbackBodyWidth, FallbackBodyHeight);
            }

            var pageWidth = GetInt(page, "width", 0);
            var pageHeight = GetInt(page, "height", 0);
            var margin = page.Element(Hp + "margin");
            var left = GetInt(margin, "left", 0);
            var right = GetInt(margin, "right", 0);
            var top = GetInt(margin, "top", 0);
            var bottom = GetInt(margin, "bottom", 0);
            var width = pageWidth - left - right;
            var height = pageHeight - top - bottom;

            return new BodyArea(
                width > 0 ? width : FallbackBodyWidth,
                height > 0 ? height : FallbackBodyHeight);
        }

        private static string FindContentPackagePath(IDictionary<string, byte[]> entries, IDictionary<string, XDocument> xmlDocuments)
        {
            if (xmlDocuments.ContainsKey("Contents/content.hpf"))
            {
                return "Contents/content.hpf";
            }

            if (xmlDocuments.ContainsKey("content.hpf"))
            {
                return "content.hpf";
            }

            return xmlDocuments.Keys.FirstOrDefault(path => path.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase) && (entries == null || entries.ContainsKey(path))) ?? string.Empty;
        }

        private static string NormalizeImageExtension(string extension)
        {
            var value = (extension ?? string.Empty).Trim().ToLowerInvariant();
            if (value == ".jpg")
            {
                return ".jpeg";
            }

            return value.Length == 0 ? ".png" : value;
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

        private static string GetString(XElement element, string attributeName)
        {
            var attribute = element == null ? null : element.Attribute(attributeName);
            return attribute == null ? string.Empty : attribute.Value;
        }

        private static int GetInt(XElement element, string attributeName, int defaultValue)
        {
            var attribute = element == null ? null : element.Attribute(attributeName);
            int value;
            return attribute != null && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
        }

        private static long GetLong(XElement element, string attributeName, long defaultValue)
        {
            var attribute = element == null ? null : element.Attribute(attributeName);
            long value;
            return attribute != null && long.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
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

        private struct ImageSize
        {
            public ImageSize(int width, int height, double horizontalDpi, double verticalDpi)
            {
                Width = width;
                Height = height;
                HorizontalDpi = horizontalDpi;
                VerticalDpi = verticalDpi;
            }

            public int Width { get; private set; }

            public int Height { get; private set; }

            public double HorizontalDpi { get; private set; }

            public double VerticalDpi { get; private set; }
        }

        private struct DisplaySize
        {
            public DisplaySize(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width { get; private set; }

            public int Height { get; private set; }
        }

        private struct BodyArea
        {
            public BodyArea(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width { get; private set; }

            public int Height { get; private set; }
        }

        private sealed class SectionDocument
        {
            public SectionDocument(string path, XDocument document)
            {
                Path = path;
                Document = document;
            }

            public string Path { get; private set; }

            public XDocument Document { get; private set; }
        }
    }
}
