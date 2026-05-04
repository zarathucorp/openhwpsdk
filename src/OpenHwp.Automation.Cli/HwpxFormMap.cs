using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml;
using OpenHwp.Automation;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxFormMap
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";
        private static readonly XNamespace Hh = "http://www.hancom.co.kr/hwpml/2011/head";
        private static readonly XNamespace Hpf = "http://www.hancom.co.kr/schema/2011/hpf";
        private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf/";

        public static void Extract(string templatePath, string outputXmlPath)
        {
            var fullTemplatePath = Path.GetFullPath(templatePath);
            var entries = SimpleZipArchive.ReadAll(fullTemplatePath);
            var xmlDocuments = ParseXmlDocuments(entries);
            var writablePartNames = GetWritablePartNames(entries, xmlDocuments);

            var root = new XElement(
                "formMap",
                new XAttribute("version", "2"),
                new XAttribute("templatePath", fullTemplatePath),
                new XAttribute("mappingScope", "package"),
                new XAttribute("generatedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));

            root.Add(new XComment("Fill writeText or writeImage elements, then run apply-form-map. HWPX XML is read only; writing is performed through HWP automation."));
            root.Add(BuildPackageElement(entries, xmlDocuments, writablePartNames));

            var tablesElement = new XElement("tables");
            var anchorsElement = new XElement("anchors");
            var tableIndex = 0;
            var anchorIndex = 0;
            var partIndex = 0;
            var anchorOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var partName in writablePartNames)
            {
                var document = xmlDocuments[partName];
                var partRole = DeterminePartRole(partName, document);
                var tableOrderInPart = 0;
                foreach (var table in document.Descendants(Hp + "tbl"))
                {
                    var tableElement = ExtractTable(table, partName, partRole, partIndex, tableIndex, tableOrderInPart);
                    tablesElement.Add(tableElement);
                    tableIndex++;
                    tableOrderInPart++;
                }

                var standaloneParagraphOrder = 0;
                var paragraphs = document.Descendants(Hp + "p").ToList();
                for (var paragraphIndex = 0; paragraphIndex < paragraphs.Count; paragraphIndex++)
                {
                    var paragraph = paragraphs[paragraphIndex];
                    if (paragraph.Ancestors(Hp + "tbl").Any() || paragraph.Descendants(Hp + "tbl").Any())
                    {
                        continue;
                    }

                    var text = NormalizeText(TextOf(paragraph));
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var occurrence = 0;
                    if (anchorOccurrences.ContainsKey(text))
                    {
                        occurrence = anchorOccurrences[text];
                    }

                    anchorOccurrences[text] = occurrence + 1;
                    anchorsElement.Add(ExtractAnchor(paragraph, partName, partRole, partIndex, anchorIndex, standaloneParagraphOrder, paragraphIndex, occurrence, text));
                    anchorIndex++;
                    standaloneParagraphOrder++;
                }

                partIndex++;
            }

            root.Add(tablesElement);
            root.Add(anchorsElement);

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputXmlPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root)
                .Save(outputXmlPath);
        }

        public static ApplyResult Apply(HwpSession hwp, string mapPath, int maxOperations)
        {
            if (hwp == null)
            {
                throw new ArgumentNullException(nameof(hwp));
            }

            var document = XDocument.Load(mapPath, LoadOptions.PreserveWhitespace);
            var mapDirectory = Path.GetDirectoryName(Path.GetFullPath(mapPath)) ?? Directory.GetCurrentDirectory();
            var result = new ApplyResult();

            foreach (var cell in document.Descendants("cell"))
            {
                if (LimitReached(result, maxOperations))
                {
                    break;
                }

                ApplyCellWrites(hwp, cell, mapDirectory, result, maxOperations);
            }

            foreach (var anchor in document.Descendants("anchor"))
            {
                if (LimitReached(result, maxOperations))
                {
                    break;
                }

                ApplyAnchorWrites(hwp, anchor, mapDirectory, result, maxOperations);
            }

            return result;
        }

        public static ApplyResult ApplyPackage(string inputHwpxPath, string mapPath, string outputHwpxPath, int maxOperations)
        {
            var entries = SimpleZipArchive.ReadAll(Path.GetFullPath(inputHwpxPath));
            var xmlDocuments = ParseXmlDocuments(entries);
            var mapDocument = XDocument.Load(mapPath, LoadOptions.PreserveWhitespace);
            var touchedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new ApplyResult();

            foreach (var cell in mapDocument.Descendants("cell"))
            {
                if (LimitReached(result, maxOperations))
                {
                    break;
                }

                ApplyPackageCellWrites(xmlDocuments, touchedParts, cell, result, maxOperations);
            }

            foreach (var anchor in mapDocument.Descendants("anchor"))
            {
                if (LimitReached(result, maxOperations))
                {
                    break;
                }

                ApplyPackageAnchorWrites(xmlDocuments, touchedParts, anchor, result, maxOperations);
            }

            foreach (var partPath in touchedParts)
            {
                entries[partPath] = SerializeXmlDocument(xmlDocuments[partPath]);
            }

            if (touchedParts.Count > 0)
            {
                UpdatePreviewText(entries, xmlDocuments);
            }

            SimpleZipArchive.WriteAll(outputHwpxPath, entries);
            return result;
        }

        public static ProbeResult Probe(HwpSession hwp, string mapPath, string reportPath, int maxOperations)
        {
            if (hwp == null)
            {
                throw new ArgumentNullException(nameof(hwp));
            }

            var document = XDocument.Load(mapPath, LoadOptions.PreserveWhitespace);
            var result = new ProbeResult();
            var report = new StringBuilder();
            report.AppendLine("# HWPX Form Map Probe");
            report.AppendLine();
            report.AppendLine("- map: " + Path.GetFullPath(mapPath));
            report.AppendLine("- max operations: " + (maxOperations <= 0 ? "all" : maxOperations.ToString(CultureInfo.InvariantCulture)));
            report.AppendLine();
            report.AppendLine("| kind | id | target | result | note |");
            report.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (var cell in document.Descendants("cell"))
            {
                if (ProbeLimitReached(result, maxOperations))
                {
                    break;
                }

                ProbeCell(hwp, cell, result, report);
            }

            foreach (var anchor in document.Descendants("anchor"))
            {
                if (ProbeLimitReached(result, maxOperations))
                {
                    break;
                }

                ProbeAnchor(hwp, anchor, result, report);
            }

            report.AppendLine();
            report.AppendLine("## Summary");
            report.AppendLine();
            report.AppendLine("- attempted: " + result.Attempted);
            report.AppendLine("- passed: " + result.Passed);
            report.AppendLine("- failed: " + result.Failed);
            report.AppendLine("- skipped: " + result.Skipped);

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            }

            return result;
        }

        private static Dictionary<string, XDocument> ParseXmlDocuments(IDictionary<string, byte[]> entries)
        {
            var documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!IsXmlLikePart(entry.Key))
                {
                    continue;
                }

                try
                {
                    documents[entry.Key] = XDocument.Parse(Encoding.UTF8.GetString(entry.Value), LoadOptions.PreserveWhitespace);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("Failed to parse XML part: " + entry.Key, ex);
                }
            }

            return documents;
        }

        private static bool IsXmlLikePart(string path)
        {
            return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".rdf", StringComparison.OrdinalIgnoreCase);
        }

        private static XElement BuildPackageElement(IDictionary<string, byte[]> entries, IDictionary<string, XDocument> xmlDocuments, IList<string> writablePartNames)
        {
            var package = new XElement(
                "package",
                new XAttribute("entryCount", entries.Count.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("xmlPartCount", xmlDocuments.Count.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("writablePartCount", writablePartNames.Count.ToString(CultureInfo.InvariantCulture)));

            package.Add(BuildRootfilesElement(xmlDocuments));
            package.Add(BuildContentPackageElement(entries, xmlDocuments));

            var entriesElement = new XElement("entries");
            foreach (var entry in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                entriesElement.Add(
                    new XElement(
                        "entry",
                        new XAttribute("path", entry.Key),
                        new XAttribute("length", entry.Value.Length.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("kind", DetermineEntryKind(entry.Key)),
                        new XAttribute("xml", xmlDocuments.ContainsKey(entry.Key) ? "true" : "false"),
                        new XAttribute("writeCandidate", writablePartNames.Contains(entry.Key, StringComparer.OrdinalIgnoreCase) ? "true" : "false")));
            }

            package.Add(entriesElement);

            var xmlPartsElement = new XElement("xmlParts");
            foreach (var item in xmlDocuments.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                xmlPartsElement.Add(BuildXmlPartElement(item.Key, item.Value, writablePartNames.Contains(item.Key, StringComparer.OrdinalIgnoreCase)));
            }

            package.Add(xmlPartsElement);
            return package;
        }

        private static XElement BuildRootfilesElement(IDictionary<string, XDocument> xmlDocuments)
        {
            var rootfilesElement = new XElement("rootfiles");
            XDocument container;
            if (!xmlDocuments.TryGetValue("META-INF/container.xml", out container))
            {
                return rootfilesElement;
            }

            foreach (var rootfile in container.Descendants().Where(element => element.Name.LocalName == "rootfile"))
            {
                rootfilesElement.Add(
                    new XElement(
                        "rootfile",
                        new XAttribute("path", GetString(rootfile, "full-path")),
                        new XAttribute("mediaType", GetString(rootfile, "media-type"))));
            }

            return rootfilesElement;
        }

        private static XElement BuildContentPackageElement(IDictionary<string, byte[]> entries, IDictionary<string, XDocument> xmlDocuments)
        {
            var contentPackage = new XElement("contentPackage");
            var contentPackagePath = FindContentPackagePath(xmlDocuments);
            if (string.IsNullOrWhiteSpace(contentPackagePath))
            {
                return contentPackage;
            }

            var document = xmlDocuments[contentPackagePath];
            contentPackage.SetAttributeValue("path", contentPackagePath);
            contentPackage.Add(BuildMetadataElement(document));
            contentPackage.Add(BuildManifestElement(entries, contentPackagePath, document));
            contentPackage.Add(BuildSpineElement(entries, contentPackagePath, document));
            return contentPackage;
        }

        private static XElement BuildMetadataElement(XDocument document)
        {
            var metadataElement = new XElement("metadata");
            var metadata = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "metadata");
            if (metadata == null)
            {
                return metadataElement;
            }

            foreach (var item in metadata.Elements())
            {
                if (item.Name.LocalName == "meta")
                {
                    metadataElement.Add(
                        new XElement(
                            "meta",
                            new XAttribute("name", GetString(item, "name")),
                            new XAttribute("contentType", GetString(item, "content")),
                            item.Value ?? string.Empty));
                }
                else
                {
                    metadataElement.Add(
                        new XElement(
                            item.Name.LocalName,
                            item.Value ?? string.Empty));
                }
            }

            return metadataElement;
        }

        private static XElement BuildManifestElement(IDictionary<string, byte[]> entries, string contentPackagePath, XDocument document)
        {
            var manifestElement = new XElement("manifest");
            foreach (var item in GetHpfManifestItems(document))
            {
                var href = GetString(item, "href");
                var resolvedPath = ResolvePackageHref(contentPackagePath, href, entries);
                manifestElement.Add(
                    new XElement(
                        "item",
                        new XAttribute("id", GetString(item, "id")),
                        new XAttribute("href", href),
                        new XAttribute("path", resolvedPath),
                        new XAttribute("mediaType", GetString(item, "media-type")),
                        new XAttribute("exists", entries.ContainsKey(resolvedPath) ? "true" : "false")));
            }

            return manifestElement;
        }

        private static XElement BuildSpineElement(IDictionary<string, byte[]> entries, string contentPackagePath, XDocument document)
        {
            var spineElement = new XElement("spine");
            var manifestById = GetHpfManifestItems(document)
                .GroupBy(item => GetString(item, "id"), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var order = 0;
            foreach (var itemref in document.Descendants().Where(element => element.Name.LocalName == "itemref"))
            {
                var idref = GetString(itemref, "idref");
                var href = manifestById.ContainsKey(idref) ? GetString(manifestById[idref], "href") : string.Empty;
                var resolvedPath = string.IsNullOrWhiteSpace(href) ? string.Empty : ResolvePackageHref(contentPackagePath, href, entries);
                spineElement.Add(
                    new XElement(
                        "itemref",
                        new XAttribute("order", order.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("idref", idref),
                        new XAttribute("href", href),
                        new XAttribute("path", resolvedPath),
                        new XAttribute("linear", GetString(itemref, "linear")),
                        new XAttribute("exists", !string.IsNullOrWhiteSpace(resolvedPath) && entries.ContainsKey(resolvedPath) ? "true" : "false")));
                order++;
            }

            return spineElement;
        }

        private static XElement BuildXmlPartElement(string path, XDocument document, bool writeCandidate)
        {
            var root = document.Root;
            var partElement = new XElement(
                "part",
                new XAttribute("path", path),
                new XAttribute("role", DeterminePartRole(path, document)),
                new XAttribute("rootName", root == null ? string.Empty : root.Name.LocalName),
                new XAttribute("rootNamespace", root == null ? string.Empty : root.Name.NamespaceName),
                new XAttribute("elementCount", document.Descendants().Count().ToString(CultureInfo.InvariantCulture)),
                new XAttribute("paragraphCount", document.Descendants(Hp + "p").Count().ToString(CultureInfo.InvariantCulture)),
                new XAttribute("tableCount", document.Descendants(Hp + "tbl").Count().ToString(CultureInfo.InvariantCulture)),
                new XAttribute("cellCount", document.Descendants(Hp + "tc").Count().ToString(CultureInfo.InvariantCulture)),
                new XAttribute("textRunCount", document.Descendants(Hp + "t").Count().ToString(CultureInfo.InvariantCulture)),
                new XAttribute("writeCandidate", writeCandidate ? "true" : "false"));

            if (path.EndsWith("header.xml", StringComparison.OrdinalIgnoreCase) || (root != null && root.Name == Hh + "head"))
            {
                partElement.Add(
                    new XElement(
                        "referenceCounts",
                        new XAttribute("fontface", document.Descendants(Hh + "fontface").Count().ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("font", document.Descendants(Hh + "font").Count().ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("charPr", document.Descendants(Hh + "charPr").Count().ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("paraPr", document.Descendants(Hh + "paraPr").Count().ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("borderFill", document.Descendants(Hh + "borderFill").Count().ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("style", document.Descendants(Hh + "style").Count().ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("tabPr", document.Descendants(Hh + "tabPr").Count().ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("numbering", document.Descendants(Hh + "numbering").Count().ToString(CultureInfo.InvariantCulture))));
            }

            return partElement;
        }

        private static IList<XElement> GetHpfManifestItems(XDocument document)
        {
            var manifest = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "manifest" && element.Name.NamespaceName == Opf.NamespaceName);
            if (manifest == null)
            {
                manifest = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "manifest");
            }

            return manifest == null
                ? new List<XElement>()
                : manifest.Elements().Where(element => element.Name.LocalName == "item").ToList();
        }

        private static List<string> GetWritablePartNames(IDictionary<string, byte[]> entries, IDictionary<string, XDocument> xmlDocuments)
        {
            var result = new List<string>();
            var contentPackagePath = FindContentPackagePath(xmlDocuments);
            if (!string.IsNullOrWhiteSpace(contentPackagePath))
            {
                foreach (var partPath in GetSpinePartPaths(entries, contentPackagePath, xmlDocuments[contentPackagePath]))
                {
                    XDocument document;
                    if (xmlDocuments.TryGetValue(partPath, out document) && HasHwpWritingContent(document) && !ContainsPath(result, partPath))
                    {
                        result.Add(partPath);
                    }
                }
            }

            foreach (var item in xmlDocuments.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (HasHwpWritingContent(item.Value) && !ContainsPath(result, item.Key))
                {
                    result.Add(item.Key);
                }
            }

            return result;
        }

        private static IEnumerable<string> GetSpinePartPaths(IDictionary<string, byte[]> entries, string contentPackagePath, XDocument document)
        {
            var manifestById = GetHpfManifestItems(document)
                .GroupBy(item => GetString(item, "id"), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var itemref in document.Descendants().Where(element => element.Name.LocalName == "itemref"))
            {
                var idref = GetString(itemref, "idref");
                if (!manifestById.ContainsKey(idref))
                {
                    continue;
                }

                var href = GetString(manifestById[idref], "href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    yield return ResolvePackageHref(contentPackagePath, href, entries);
                }
            }
        }

        private static bool HasHwpWritingContent(XDocument document)
        {
            return document.Descendants(Hp + "p").Any() ||
                   document.Descendants(Hp + "tbl").Any() ||
                   document.Descendants(Hp + "t").Any();
        }

        private static bool ContainsPath(IEnumerable<string> paths, string path)
        {
            return paths.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        }

        private static string FindContentPackagePath(IDictionary<string, XDocument> xmlDocuments)
        {
            if (xmlDocuments.ContainsKey("Contents/content.hpf"))
            {
                return "Contents/content.hpf";
            }

            return xmlDocuments.Keys.FirstOrDefault(path => path.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static string ResolvePackageHref(string contentPackagePath, string href, IDictionary<string, byte[]> entries)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return string.Empty;
            }

            var normalized = href.Replace('\\', '/').TrimStart('/');
            if (entries.ContainsKey(normalized))
            {
                return normalized;
            }

            var baseDirectory = PackageDirectoryName(contentPackagePath);
            var combined = string.IsNullOrWhiteSpace(baseDirectory) ? normalized : baseDirectory + "/" + normalized;
            return NormalizePackagePath(combined);
        }

        private static string PackageDirectoryName(string path)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/');
            var index = normalized.LastIndexOf('/');
            return index < 0 ? string.Empty : normalized.Substring(0, index);
        }

        private static string NormalizePackagePath(string path)
        {
            var parts = new List<string>();
            foreach (var part in (path ?? string.Empty).Replace('\\', '/').Split('/'))
            {
                if (string.IsNullOrWhiteSpace(part) || part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (parts.Count > 0)
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }

                    continue;
                }

                parts.Add(part);
            }

            return string.Join("/", parts.ToArray());
        }

        private static string DetermineEntryKind(string path)
        {
            if (path.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase))
            {
                return IsXmlLikePart(path) ? "contentXml" : "contentResource";
            }

            if (path.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase))
            {
                return "binaryData";
            }

            if (path.StartsWith("Preview/", StringComparison.OrdinalIgnoreCase))
            {
                return "preview";
            }

            if (path.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                return "packageMeta";
            }

            return IsXmlLikePart(path) ? "packageXml" : "package";
        }

        private static string DeterminePartRole(string path, XDocument document)
        {
            var root = document.Root;
            if (path.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase) || (root != null && root.Name == Hpf + "package"))
            {
                return "contentPackage";
            }

            if (path.EndsWith("header.xml", StringComparison.OrdinalIgnoreCase) || (root != null && root.Name == Hh + "head"))
            {
                return "documentHead";
            }

            if (path.IndexOf("/section", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "bodySection";
            }

            if (path.EndsWith("settings.xml", StringComparison.OrdinalIgnoreCase))
            {
                return "settings";
            }

            if (path.EndsWith("version.xml", StringComparison.OrdinalIgnoreCase))
            {
                return "version";
            }

            if (path.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            {
                return "packageMeta";
            }

            return "xmlPart";
        }

        private static XElement ExtractTable(XElement table, string partName, string partRole, int partIndex, int tableIndex, int tableOrderInPart)
        {
            var rows = table.Elements(Hp + "tr").ToList();
            var cells = rows.SelectMany(row => row.Elements(Hp + "tc")).ToList();
            var hasMergedCells = cells.Any(IsMergedCell);
            var size = table.Element(Hp + "sz");
            var tableElement = new XElement(
                "table",
                new XAttribute("id", "table-" + tableIndex.ToString("000", CultureInfo.InvariantCulture)),
                new XAttribute("tableIndex", tableIndex.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("partPath", partName),
                new XAttribute("partRole", partRole),
                new XAttribute("partIndex", partIndex.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("section", partName),
                new XAttribute("tableOrderInPart", tableOrderInPart.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("writeSupported", "true"),
                new XAttribute("rows", GetInt(table, "rowCnt", rows.Count).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("cols", GetInt(table, "colCnt", MaxColumnCount(table)).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("width", (size == null ? 0 : GetInt(size, "width", 0)).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("borderFillId", GetString(table, "borderFillIDRef")),
                new XAttribute("hasMergedCells", hasMergedCells ? "true" : "false"));

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var rowElement = new XElement(
                    "row",
                    new XAttribute("row", rowIndex.ToString(CultureInfo.InvariantCulture)));

                var rowCells = row.Elements(Hp + "tc").ToList();
                for (var cellIndex = 0; cellIndex < rowCells.Count; cellIndex++)
                {
                    var cell = rowCells[cellIndex];
                    var cellAddr = cell.Element(Hp + "cellAddr");
                    var cellSpan = cell.Element(Hp + "cellSpan");
                    var cellSz = cell.Element(Hp + "cellSz");
                    var rowAddress = cellAddr == null ? rowIndex : GetInt(cellAddr, "rowAddr", rowIndex);
                    var colAddress = cellAddr == null ? cellIndex : GetInt(cellAddr, "colAddr", cellIndex);
                    var rowSpan = cellSpan == null ? 1 : GetInt(cellSpan, "rowSpan", 1);
                    var colSpan = cellSpan == null ? 1 : GetInt(cellSpan, "colSpan", 1);
                    var merged = rowSpan > 1 || colSpan > 1;
                    var firstParagraph = cell.Descendants(Hp + "p").FirstOrDefault();
                    var firstRun = firstParagraph == null ? null : firstParagraph.Descendants(Hp + "run").FirstOrDefault();

                    rowElement.Add(
                        new XElement(
                            "cell",
                            new XAttribute("id", string.Format(CultureInfo.InvariantCulture, "table-{0:000}-r{1:000}-c{2:000}", tableIndex, rowAddress, colAddress)),
                            new XAttribute("tableIndex", tableIndex.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("row", rowAddress.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("col", colAddress.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("rowOrder", rowIndex.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("cellOrder", cellIndex.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("rowSpan", rowSpan.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("colSpan", colSpan.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("merged", merged ? "true" : "false"),
                            new XAttribute("borderFillId", GetString(cell, "borderFillIDRef")),
                            new XAttribute("width", (cellSz == null ? 0 : GetInt(cellSz, "width", 0)).ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("height", (cellSz == null ? 0 : GetInt(cellSz, "height", 0)).ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("paraPrId", firstParagraph == null ? string.Empty : GetString(firstParagraph, "paraPrIDRef")),
                            new XAttribute("styleId", firstParagraph == null ? string.Empty : GetString(firstParagraph, "styleIDRef")),
                            new XAttribute("charPrId", firstRun == null ? string.Empty : GetString(firstRun, "charPrIDRef")),
                            new XElement("currentText", NormalizeText(TextOf(cell))),
                            new XElement("writeText"),
                            new XElement("writeImage",
                                new XAttribute("path", string.Empty),
                                new XAttribute("width", "200"),
                                new XAttribute("height", "200"),
                                new XAttribute("clearCell", "true"))));
                }

                tableElement.Add(rowElement);
            }

            return tableElement;
        }

        private static XElement ExtractAnchor(
            XElement paragraph,
            string partName,
            string partRole,
            int partIndex,
            int anchorIndex,
            int anchorOrderInPart,
            int paragraphIndexInPart,
            int occurrence,
            string text)
        {
            var firstRun = paragraph.Descendants(Hp + "run").FirstOrDefault();
            return new XElement(
                "anchor",
                new XAttribute("id", "anchor-" + anchorIndex.ToString("0000", CultureInfo.InvariantCulture)),
                new XAttribute("partPath", partName),
                new XAttribute("partRole", partRole),
                new XAttribute("partIndex", partIndex.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("section", partName),
                new XAttribute("paragraphIndex", paragraphIndexInPart.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("anchorOrderInPart", anchorOrderInPart.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("occurrence", occurrence.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("paraId", GetString(paragraph, "id")),
                new XAttribute("paraPrId", GetString(paragraph, "paraPrIDRef")),
                new XAttribute("styleId", GetString(paragraph, "styleIDRef")),
                new XAttribute("charPrId", firstRun == null ? string.Empty : GetString(firstRun, "charPrIDRef")),
                new XAttribute("writeSupported", "true"),
                new XElement("currentText", text),
                new XElement("writeText", new XAttribute("replaceAnchorText", "true")),
                new XElement("writeImage",
                    new XAttribute("path", string.Empty),
                    new XAttribute("width", "200"),
                    new XAttribute("height", "200"),
                    new XAttribute("removeAnchorText", "false")));
        }

        private static void ApplyCellWrites(HwpSession hwp, XElement cell, string mapDirectory, ApplyResult result, int maxOperations)
        {
            var tableIndex = GetRequiredInt(cell, "tableIndex");
            var row = GetRequiredInt(cell, "row");
            var col = GetRequiredInt(cell, "col");
            var writeSupported = GetBool(cell, "writeSupported", true);
            if (!writeSupported)
            {
                if (HasTextWrite(cell) || HasImageWrite(cell))
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    result.SkippedUnsafe++;
                    Console.WriteLine("skip unsupported cell write: table={0}, row={1}, col={2}", tableIndex, row, col);
                }

                return;
            }

            var isMerged = GetBool(cell, "merged", false);
            var force = GetBool(cell, "force", false);
            if (isMerged && !force)
            {
                if (HasTextWrite(cell) || HasImageWrite(cell))
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    result.SkippedUnsafe++;
                    Console.WriteLine("skip merged cell without force=true: table={0}, row={1}, col={2}", tableIndex, row, col);
                }

                return;
            }

            var writeText = cell.Element("writeText");
            if (writeText != null)
            {
                var shouldClear = GetBool(writeText, "clear", false);
                var text = writeText.Value ?? string.Empty;
                if (shouldClear || text.Length > 0)
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    Console.WriteLine("set cell: table={0}, row={1}, col={2}, text={3}", tableIndex, row, col, Abbreviate(text, 80));
                    if (!hwp.SetTableCellText(tableIndex, row, col, text))
                    {
                        result.Failed++;
                        Console.WriteLine("  failed");
                    }
                    else
                    {
                        result.Applied++;
                    }
                }
            }

            var writeImage = cell.Element("writeImage");
            if (writeImage != null)
            {
                var path = GetString(writeImage, "path");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    var resolvedPath = ResolveRelativePath(mapDirectory, path);
                    var width = GetInt(writeImage, "width", 200);
                    var height = GetInt(writeImage, "height", 200);
                    var clearCell = GetBool(writeImage, "clearCell", true);
                    Console.WriteLine("insert cell image: table={0}, row={1}, col={2}, path={3}", tableIndex, row, col, resolvedPath);
                    if (!hwp.InsertPictureInTableCell(tableIndex, row, col, resolvedPath, width, height, clearCell))
                    {
                        result.Failed++;
                        Console.WriteLine("  failed");
                    }
                    else
                    {
                        result.Applied++;
                    }
                }
            }
        }

        private static void ApplyAnchorWrites(HwpSession hwp, XElement anchor, string mapDirectory, ApplyResult result, int maxOperations)
        {
            var currentText = (anchor.Element("currentText") == null ? string.Empty : anchor.Element("currentText").Value).Trim();
            if (string.IsNullOrWhiteSpace(currentText))
            {
                return;
            }

            var occurrence = GetInt(anchor, "occurrence", 0);
            var writeSupported = GetBool(anchor, "writeSupported", true);
            if (!writeSupported)
            {
                if (HasTextWrite(anchor) || HasImageWrite(anchor))
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    result.SkippedUnsafe++;
                    Console.WriteLine("skip unsupported anchor write: anchor={0}", Abbreviate(currentText, 80));
                }

                return;
            }

            var writeText = anchor.Element("writeText");
            if (writeText != null)
            {
                var shouldClear = GetBool(writeText, "clear", false);
                var text = writeText.Value ?? string.Empty;
                if (shouldClear || text.Length > 0)
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    var replaceAnchorText = GetBool(writeText, "replaceAnchorText", true);
                    Console.WriteLine("set anchor text: anchor={0}, text={1}", Abbreviate(currentText, 80), Abbreviate(text, 80));
                    if (!hwp.WriteTextAtTextAnchor(currentText, text, replaceAnchorText, occurrence))
                    {
                        result.Failed++;
                        Console.WriteLine("  failed");
                    }
                    else
                    {
                        result.Applied++;
                    }
                }
            }

            var writeImage = anchor.Element("writeImage");
            if (writeImage == null)
            {
                return;
            }

            var path = GetString(writeImage, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!BeginOperation(result, maxOperations))
            {
                return;
            }

            var resolvedPath = ResolveRelativePath(mapDirectory, path);
            var width = GetInt(writeImage, "width", 200);
            var height = GetInt(writeImage, "height", 200);
            var removeAnchorText = GetBool(writeImage, "removeAnchorText", false);
            Console.WriteLine("insert anchor image: anchor={0}, path={1}", Abbreviate(currentText, 80), resolvedPath);
            if (!hwp.InsertPictureAtTextAnchor(currentText, resolvedPath, width, height, removeAnchorText, occurrence))
            {
                result.Failed++;
                Console.WriteLine("  failed");
            }
            else
            {
                result.Applied++;
            }
        }

        private static void ApplyPackageCellWrites(IDictionary<string, XDocument> xmlDocuments, ISet<string> touchedParts, XElement cell, ApplyResult result, int maxOperations)
        {
            var writeSupported = GetBool(cell, "writeSupported", true);
            if (!writeSupported)
            {
                if (HasTextWrite(cell) || HasImageWrite(cell))
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    result.SkippedUnsafe++;
                    Console.WriteLine("skip unsupported package cell write: id={0}", GetString(cell, "id"));
                }

                return;
            }

            var writeText = cell.Element("writeText");
            if (writeText != null)
            {
                var shouldClear = GetBool(writeText, "clear", false);
                var text = writeText.Value ?? string.Empty;
                if (shouldClear || text.Length > 0)
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    string reason;
                    if (TryApplyPackageCellText(xmlDocuments, touchedParts, cell, text, out reason))
                    {
                        result.Applied++;
                        Console.WriteLine("package set cell: id={0}, text={1}", GetString(cell, "id"), Abbreviate(text, 80));
                    }
                    else
                    {
                        result.Failed++;
                        Console.WriteLine("package set cell failed: id={0}, reason={1}", GetString(cell, "id"), reason);
                    }
                }
            }

            var writeImage = cell.Element("writeImage");
            if (writeImage != null && !string.IsNullOrWhiteSpace(GetString(writeImage, "path")))
            {
                if (!BeginOperation(result, maxOperations))
                {
                    return;
                }

                result.SkippedUnsafe++;
                Console.WriteLine("skip package cell image write: id={0}", GetString(cell, "id"));
            }
        }

        private static void ApplyPackageAnchorWrites(IDictionary<string, XDocument> xmlDocuments, ISet<string> touchedParts, XElement anchor, ApplyResult result, int maxOperations)
        {
            var writeSupported = GetBool(anchor, "writeSupported", true);
            if (!writeSupported)
            {
                if (HasTextWrite(anchor) || HasImageWrite(anchor))
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    result.SkippedUnsafe++;
                    Console.WriteLine("skip unsupported package anchor write: id={0}", GetString(anchor, "id"));
                }

                return;
            }

            var writeText = anchor.Element("writeText");
            if (writeText != null)
            {
                var shouldClear = GetBool(writeText, "clear", false);
                var text = writeText.Value ?? string.Empty;
                if (shouldClear || text.Length > 0)
                {
                    if (!BeginOperation(result, maxOperations))
                    {
                        return;
                    }

                    string reason;
                    if (TryApplyPackageAnchorText(xmlDocuments, touchedParts, anchor, text, out reason))
                    {
                        result.Applied++;
                        Console.WriteLine("package set anchor: id={0}, text={1}", GetString(anchor, "id"), Abbreviate(text, 80));
                    }
                    else
                    {
                        result.Failed++;
                        Console.WriteLine("package set anchor failed: id={0}, reason={1}", GetString(anchor, "id"), reason);
                    }
                }
            }

            var writeImage = anchor.Element("writeImage");
            if (writeImage != null && !string.IsNullOrWhiteSpace(GetString(writeImage, "path")))
            {
                if (!BeginOperation(result, maxOperations))
                {
                    return;
                }

                result.SkippedUnsafe++;
                Console.WriteLine("skip package anchor image write: id={0}", GetString(anchor, "id"));
            }
        }

        private static bool TryApplyPackageCellText(IDictionary<string, XDocument> xmlDocuments, ISet<string> touchedParts, XElement mapCell, string text, out string reason)
        {
            var partPath = ResolveMapPartPath(mapCell);
            if (string.IsNullOrWhiteSpace(partPath))
            {
                reason = "map cell does not include partPath";
                return false;
            }

            XDocument document;
            if (!xmlDocuments.TryGetValue(partPath, out document))
            {
                reason = "package part was not found: " + partPath;
                return false;
            }

            XElement table;
            if (!TryResolvePackageTable(document, mapCell, out table, out reason))
            {
                return false;
            }

            XElement targetCell;
            if (!TryResolvePackageTableCell(table, mapCell, out targetCell, out reason))
            {
                return false;
            }

            if (!TrySetCellTextPreservingNestedContent(targetCell, text, out reason))
            {
                return false;
            }

            touchedParts.Add(partPath);
            return true;
        }

        private static bool TryApplyPackageAnchorText(IDictionary<string, XDocument> xmlDocuments, ISet<string> touchedParts, XElement mapAnchor, string text, out string reason)
        {
            var partPath = ResolveMapPartPath(mapAnchor);
            if (string.IsNullOrWhiteSpace(partPath))
            {
                reason = "map anchor does not include partPath";
                return false;
            }

            XDocument document;
            if (!xmlDocuments.TryGetValue(partPath, out document))
            {
                reason = "package part was not found: " + partPath;
                return false;
            }

            XElement paragraph;
            if (!TryResolvePackageAnchorParagraph(document, mapAnchor, out paragraph, out reason))
            {
                return false;
            }

            var replaceAnchorText = GetBool(mapAnchor.Element("writeText"), "replaceAnchorText", true);
            var finalText = replaceAnchorText ? text : TextOf(paragraph) + text;
            if (!TrySetParagraphText(paragraph, finalText, out reason))
            {
                return false;
            }

            touchedParts.Add(partPath);
            return true;
        }

        private static string ResolveMapPartPath(XElement mapElement)
        {
            var partPath = GetString(mapElement, "partPath");
            if (!string.IsNullOrWhiteSpace(partPath))
            {
                return partPath;
            }

            var table = mapElement.Ancestors("table").FirstOrDefault();
            return table == null ? string.Empty : GetString(table, "partPath");
        }

        private static bool TryResolvePackageTable(XDocument document, XElement mapCell, out XElement table, out string reason)
        {
            var mapTable = mapCell.Ancestors("table").FirstOrDefault();
            var tableOrderInPart = mapTable == null ? -1 : GetInt(mapTable, "tableOrderInPart", -1);
            var tables = document.Descendants(Hp + "tbl").ToList();
            if (tableOrderInPart >= 0 && tableOrderInPart < tables.Count)
            {
                table = tables[tableOrderInPart];
                reason = string.Empty;
                return true;
            }

            var tableIndex = GetInt(mapCell, "tableIndex", -1);
            if (tableIndex >= 0 && tableIndex < tables.Count)
            {
                table = tables[tableIndex];
                reason = string.Empty;
                return true;
            }

            table = null;
            reason = "table was not found in package part";
            return false;
        }

        private static bool TryResolvePackageTableCell(XElement table, XElement mapCell, out XElement targetCell, out string reason)
        {
            var rowOrder = GetInt(mapCell, "rowOrder", -1);
            var cellOrder = GetInt(mapCell, "cellOrder", -1);
            var rows = table.Elements(Hp + "tr").ToList();
            if (rowOrder >= 0 && rowOrder < rows.Count)
            {
                var cells = rows[rowOrder].Elements(Hp + "tc").ToList();
                if (cellOrder >= 0 && cellOrder < cells.Count)
                {
                    targetCell = cells[cellOrder];
                    reason = string.Empty;
                    return true;
                }
            }

            var row = GetInt(mapCell, "row", -1);
            var col = GetInt(mapCell, "col", -1);
            targetCell = table.Descendants(Hp + "tc").FirstOrDefault(cell =>
            {
                var cellAddr = cell.Element(Hp + "cellAddr");
                return cellAddr != null &&
                       GetInt(cellAddr, "rowAddr", -1) == row &&
                       GetInt(cellAddr, "colAddr", -1) == col;
            });

            if (targetCell != null)
            {
                reason = string.Empty;
                return true;
            }

            reason = "cell was not found in package table";
            return false;
        }

        private static bool TryResolvePackageAnchorParagraph(XDocument document, XElement mapAnchor, out XElement paragraph, out string reason)
        {
            var paragraphIndex = GetInt(mapAnchor, "paragraphIndex", -1);
            var paragraphs = document.Descendants(Hp + "p").ToList();
            var currentText = NormalizeText(mapAnchor.Element("currentText") == null ? string.Empty : mapAnchor.Element("currentText").Value);

            if (paragraphIndex >= 0 && paragraphIndex < paragraphs.Count)
            {
                paragraph = paragraphs[paragraphIndex];
                if (string.IsNullOrWhiteSpace(currentText) || string.Equals(NormalizeText(TextOf(paragraph)), currentText, StringComparison.Ordinal))
                {
                    reason = string.Empty;
                    return true;
                }
            }

            var occurrence = GetInt(mapAnchor, "occurrence", 0);
            paragraph = FindParagraphByTextOccurrence(paragraphs, currentText, occurrence);
            if (paragraph != null)
            {
                reason = string.Empty;
                return true;
            }

            reason = "anchor paragraph was not found";
            return false;
        }

        private static XElement FindParagraphByTextOccurrence(IList<XElement> paragraphs, string text, int occurrence)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var seen = 0;
            foreach (var paragraph in paragraphs)
            {
                if (!string.Equals(NormalizeText(TextOf(paragraph)), text, StringComparison.Ordinal))
                {
                    continue;
                }

                if (seen == occurrence)
                {
                    return paragraph;
                }

                seen++;
            }

            return null;
        }

        private static bool TrySetCellTextPreservingNestedContent(XElement cell, string text, out string reason)
        {
            var paragraph = GetWritableCellParagraph(cell);
            if (paragraph == null)
            {
                reason = "cell has no non-nested writable paragraph";
                return false;
            }

            return TrySetParagraphText(paragraph, text, out reason);
        }

        private static XElement GetWritableCellParagraph(XElement cell)
        {
            var subList = cell.Element(Hp + "subList");
            var paragraphs = subList == null ? cell.Elements(Hp + "p") : subList.Elements(Hp + "p");
            return paragraphs.FirstOrDefault(paragraph => !paragraph.Descendants(Hp + "tbl").Any());
        }

        private static bool TrySetParagraphText(XElement paragraph, string text, out string reason)
        {
            if (paragraph == null)
            {
                reason = "paragraph is missing";
                return false;
            }

            var textNodes = paragraph.Descendants(Hp + "t").ToList();
            XElement firstTextNode;
            if (textNodes.Count == 0)
            {
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

                firstTextNode = new XElement(Hp + "t");
                run.Add(firstTextNode);
                textNodes.Add(firstTextNode);
            }
            else
            {
                firstTextNode = textNodes[0];
            }

            firstTextNode.Value = NormalizePackageWriteText(text);
            for (var index = 1; index < textNodes.Count; index++)
            {
                textNodes[index].Value = string.Empty;
            }

            var staleLineSegments = paragraph.Elements(Hp + "linesegarray").ToList();
            foreach (var lineSegments in staleLineSegments)
            {
                lineSegments.Remove();
            }

            reason = string.Empty;
            return true;
        }

        private static void UpdatePreviewText(IDictionary<string, byte[]> entries, IDictionary<string, XDocument> xmlDocuments)
        {
            if (!entries.ContainsKey("Preview/PrvText.txt"))
            {
                return;
            }

            var builder = new StringBuilder();
            foreach (var partName in GetWritablePartNames(entries, xmlDocuments))
            {
                XDocument document;
                if (!xmlDocuments.TryGetValue(partName, out document))
                {
                    continue;
                }

                foreach (var paragraph in document.Descendants(Hp + "p"))
                {
                    var text = NormalizeText(TextOf(paragraph));
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        builder.AppendLine(text);
                    }
                }
            }

            entries["Preview/PrvText.txt"] = new UTF8Encoding(false).GetBytes(builder.ToString());
        }

        private static byte[] SerializeXmlDocument(XDocument document)
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

        private static string NormalizePackageWriteText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static void ProbeCell(HwpSession hwp, XElement cell, ProbeResult result, StringBuilder report)
        {
            result.Attempted++;
            var id = GetString(cell, "id");
            var tableIndex = GetRequiredInt(cell, "tableIndex");
            var row = GetRequiredInt(cell, "row");
            var col = GetRequiredInt(cell, "col");
            var target = string.Format(CultureInfo.InvariantCulture, "table={0}, row={1}, col={2}", tableIndex, row, col);
            var note = GetBool(cell, "merged", false) ? "merged" : string.Empty;

            try
            {
                if (hwp.SelectTableCell(row, col, tableIndex))
                {
                    result.Passed++;
                    AppendProbeRow(report, "cell", id, target, "PASS", note);
                }
                else
                {
                    result.Failed++;
                    AppendProbeRow(report, "cell", id, target, "FAIL", note);
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                AppendProbeRow(report, "cell", id, target, "ERROR", note + " " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void ProbeAnchor(HwpSession hwp, XElement anchor, ProbeResult result, StringBuilder report)
        {
            var currentText = (anchor.Element("currentText") == null ? string.Empty : anchor.Element("currentText").Value).Trim();
            if (string.IsNullOrWhiteSpace(currentText))
            {
                result.Skipped++;
                AppendProbeRow(report, "anchor", GetString(anchor, "id"), "empty", "SKIP", "empty currentText");
                return;
            }

            result.Attempted++;
            var id = GetString(anchor, "id");
            var occurrence = GetInt(anchor, "occurrence", 0);
            var target = string.Format(CultureInfo.InvariantCulture, "occurrence={0}, text={1}", occurrence, Abbreviate(currentText, 40));

            try
            {
                var matchedText = string.Empty;
                if (TryFindAnchorForProbe(hwp, currentText, occurrence, out matchedText))
                {
                    result.Passed++;
                    AppendProbeRow(
                        report,
                        "anchor",
                        id,
                        target,
                        "PASS",
                        string.Equals(matchedText, currentText, StringComparison.Ordinal) ? string.Empty : "fallback search: " + Abbreviate(matchedText, 60));
                }
                else
                {
                    result.Failed++;
                    AppendProbeRow(report, "anchor", id, target, "FAIL", "not found");
                }
            }
            catch (Exception ex)
            {
                result.Failed++;
                AppendProbeRow(report, "anchor", id, target, "ERROR", ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool TryFindAnchorForProbe(HwpSession hwp, string currentText, int occurrence, out string matchedText)
        {
            foreach (var candidate in BuildAnchorSearchCandidates(currentText))
            {
                var candidateOccurrence = string.Equals(candidate, currentText, StringComparison.Ordinal) ? occurrence : 0;
                if (hwp.FindTextOccurrence(candidate, candidateOccurrence))
                {
                    matchedText = candidate;
                    return true;
                }
            }

            matchedText = string.Empty;
            return false;
        }

        private static IEnumerable<string> BuildAnchorSearchCandidates(string text)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in EnumerateAnchorSearchCandidates(text ?? string.Empty))
            {
                var normalized = NormalizeText(candidate);
                if (normalized.Length == 0 || seen.Contains(normalized))
                {
                    continue;
                }

                seen.Add(normalized);
                yield return normalized;
            }
        }

        private static IEnumerable<string> EnumerateAnchorSearchCandidates(string text)
        {
            yield return text;

            var firstParen = text.IndexOf('(');
            if (firstParen > 0)
            {
                yield return text.Substring(0, firstParen).Trim();
            }

            var lengths = new[] { 80, 60, 40, 30, 20 };
            foreach (var length in lengths)
            {
                if (text.Length > length)
                {
                    yield return text.Substring(0, length);
                }
            }
        }

        private static void AppendProbeRow(StringBuilder report, string kind, string id, string target, string status, string note)
        {
            report.Append("| ");
            report.Append(EscapeMarkdownTable(kind));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(id));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(target));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(status));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(note ?? string.Empty));
            report.AppendLine(" |");
        }

        private static string EscapeMarkdownTable(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static bool BeginOperation(ApplyResult result, int maxOperations)
        {
            if (LimitReached(result, maxOperations))
            {
                return false;
            }

            result.Attempted++;
            return true;
        }

        private static bool LimitReached(ApplyResult result, int maxOperations)
        {
            return maxOperations > 0 && result.Attempted >= maxOperations;
        }

        private static bool ProbeLimitReached(ProbeResult result, int maxOperations)
        {
            return maxOperations > 0 && result.Attempted >= maxOperations;
        }

        private static bool HasTextWrite(XElement cell)
        {
            var writeText = cell.Element("writeText");
            return writeText != null && (GetBool(writeText, "clear", false) || !string.IsNullOrEmpty(writeText.Value));
        }

        private static bool HasImageWrite(XElement cell)
        {
            var writeImage = cell.Element("writeImage");
            return writeImage != null && !string.IsNullOrWhiteSpace(GetString(writeImage, "path"));
        }

        private static bool IsMergedCell(XElement cell)
        {
            var cellSpan = cell.Element(Hp + "cellSpan");
            if (cellSpan == null)
            {
                return false;
            }

            return GetInt(cellSpan, "rowSpan", 1) > 1 || GetInt(cellSpan, "colSpan", 1) > 1;
        }

        private static int MaxColumnCount(XElement table)
        {
            return table.Elements(Hp + "tr").Select(row => row.Elements(Hp + "tc").Count()).DefaultIfEmpty(0).Max();
        }

        private static string TextOf(XElement element)
        {
            var builder = new StringBuilder();
            foreach (var textNode in element.Descendants(Hp + "t"))
            {
                builder.Append(textNode.Value);
            }

            return builder.ToString();
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        private static string ResolveRelativePath(string baseDirectory, string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
        }

        private static int GetRequiredInt(XElement element, string attributeName)
        {
            var raw = GetString(element, attributeName);
            int value;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidOperationException("Missing or invalid integer attribute '" + attributeName + "' on " + element.Name.LocalName + ".");
            }

            return value;
        }

        private static string GetString(XElement element, string attributeName)
        {
            var attribute = element.Attribute(attributeName);
            return attribute == null ? string.Empty : attribute.Value;
        }

        private static int GetInt(XElement element, string attributeName, int defaultValue)
        {
            var attribute = element.Attribute(attributeName);
            if (attribute == null)
            {
                return defaultValue;
            }

            int value;
            return int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
        }

        private static bool GetBool(XElement element, string attributeName, bool defaultValue)
        {
            var attribute = element.Attribute(attributeName);
            if (attribute == null)
            {
                return defaultValue;
            }

            bool value;
            if (bool.TryParse(attribute.Value, out value))
            {
                return value;
            }

            return string.Equals(attribute.Value, "1", StringComparison.Ordinal);
        }

        private static string Abbreviate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        internal sealed class ApplyResult
        {
            public int Attempted { get; set; }

            public int Applied { get; set; }

            public int Failed { get; set; }

            public int SkippedUnsafe { get; set; }
        }

        internal sealed class ProbeResult
        {
            public int Attempted { get; set; }

            public int Passed { get; set; }

            public int Failed { get; set; }

            public int Skipped { get; set; }
        }
    }
}
