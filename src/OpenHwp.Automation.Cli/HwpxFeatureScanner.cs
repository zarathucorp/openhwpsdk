using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxFeatureScanner
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static ScanSummary Scan(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("Input path is required.", nameof(inputPath));
            }

            var fullPath = Path.GetFullPath(inputPath);
            var files = ResolveHwpxFiles(fullPath);
            var summary = new ScanSummary(fullPath);
            foreach (var file in files)
            {
                var result = ScanFile(file);
                summary.Files.Add(result);
                summary.Add(result);
            }

            return summary;
        }

        public static void WriteMarkdown(ScanSummary summary, string reportPath)
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
            builder.AppendLine("# HWPX Feature Scan");
            builder.AppendLine();
            builder.AppendLine("- input: " + summary.InputPath);
            builder.AppendLine("- files: " + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- package errors: " + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- XML parse errors: " + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();

            builder.AppendLine("## Aggregate Counts");
            builder.AppendLine();
            builder.AppendLine("| feature | count |");
            builder.AppendLine("| --- | ---: |");
            foreach (var item in summary.Aggregate.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                builder.AppendLine("| " + item.Key + " | " + item.Value.ToString(CultureInfo.InvariantCulture) + " |");
            }

            builder.AppendLine();
            builder.AppendLine("## Authoring Coverage");
            builder.AppendLine();
            builder.AppendLine("| area | observed | current support | gap |");
            builder.AppendLine("| --- | ---: | --- | --- |");
            AppendCoverageRow(builder, "paragraph text", summary.Count("paragraphs"), "read/write via COM and package anchors", "generic Markdown structure rendering remains limited");
            AppendCoverageRow(builder, "character/paragraph styles", summary.Count("charStyles") + summary.Count("paragraphStyles"), "preserve existing styles; guard tiny charPr on writes", "style creation/editing is not general-purpose");
            AppendCoverageRow(builder, "tables", summary.Count("tables"), "form-map extraction, package cell writes, merge/nested-table aware model", "arbitrary table authoring is template-driven");
            AppendCoverageRow(builder, "merged cells", summary.Count("mergedCells"), "detected and guarded; forced writes require explicit opt-in", "no generic merged-cell creation API");
            AppendCoverageRow(builder, "nested tables", summary.Count("nestedTables"), "detected; parent-cell text separated from nested content", "no generic nested-table creation API");
            AppendCoverageRow(builder, "images", summary.Count("pictures") + summary.Count("embeddedImages"), "COM insertion and package-level BinData/hp:pic embedding", "editor-specific wrapping/positioning needs visual verification");
            AppendCoverageRow(builder, "drawing shapes", summary.Count("drawingShapes"), "inventory only", "line/rect/ellipse/textbox/group editing is not implemented");
            AppendCoverageRow(builder, "fields/forms", summary.Count("fields") + summary.Count("formObjects"), "COM field commands exist", "package-level field/form extraction and writing are not implemented");
            AppendCoverageRow(builder, "headers/footers", summary.Count("pageHeaders") + summary.Count("pageFooters"), "not present in this corpus", "needs fixtures before implementation claims");
            AppendCoverageRow(builder, "footnotes/endnotes", summary.Count("footnotes") + summary.Count("endnotes"), "not present in this corpus", "needs fixtures before implementation claims");
            AppendCoverageRow(builder, "equations/charts/OLE", summary.Count("equations") + summary.Count("charts") + summary.Count("oleObjects"), "not present in this corpus", "needs fixtures before implementation claims");

            builder.AppendLine();
            builder.AppendLine("## Files");
            builder.AppendLine();
            builder.AppendLine("| file | tables | cells | merged | nested | pictures | bindata | shapes | fields | xml errors |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var file in summary.Files.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("| ");
                builder.Append(EscapeMarkdown(Path.GetFileName(file.Path)));
                builder.Append(" | ");
                builder.Append(file.Count("tables").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("tableCells").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("mergedCells").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("nestedTables").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("pictures").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("binDataEntries").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("drawingShapes").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append((file.Count("fields") + file.Count("formObjects")).ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
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

        private static void AppendCoverageRow(StringBuilder builder, string area, int observed, string support, string gap)
        {
            builder.Append("| ");
            builder.Append(area);
            builder.Append(" | ");
            builder.Append(observed.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(support);
            builder.Append(" | ");
            builder.Append(gap);
            builder.AppendLine(" |");
        }

        private static IList<string> ResolveHwpxFiles(string fullPath)
        {
            if (File.Exists(fullPath))
            {
                if (!fullPath.EndsWith(".hwpx", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Input file must be .hwpx: " + fullPath);
                }

                return new List<string> { fullPath };
            }

            if (!Directory.Exists(fullPath))
            {
                throw new FileNotFoundException("Input path was not found.", fullPath);
            }

            return Directory.GetFiles(fullPath, "*.hwpx", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static FileScanResult ScanFile(string path)
        {
            var result = new FileScanResult(path);
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

            result.Add("entries", entries.Count);
            result.Add("binDataEntries", entries.Keys.Count(name => name.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase)));
            result.Add("sectionParts", entries.Keys.Count(name => name.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
            result.Add("documentHeadParts", entries.Keys.Count(name => name.EndsWith("header.xml", StringComparison.OrdinalIgnoreCase)));
            result.Add("footerParts", entries.Keys.Count(name => name.EndsWith("footer.xml", StringComparison.OrdinalIgnoreCase)));

            foreach (var entry in entries.Where(item => IsXmlEntry(item.Key)))
            {
                XDocument document;
                try
                {
                    document = XDocument.Parse(Encoding.UTF8.GetString(entry.Value), LoadOptions.PreserveWhitespace);
                }
                catch (Exception ex)
                {
                    result.XmlParseErrors++;
                    result.XmlErrors.Add(entry.Key + ": " + ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

                ScanDocument(document, result);
            }

            return result;
        }

        private static void ScanDocument(XDocument document, FileScanResult result)
        {
            var tables = document.Descendants(Hp + "tbl").ToList();
            result.Add("tables", tables.Count);
            result.Add("tableCells", document.Descendants(Hp + "tc").Count());
            result.Add("paragraphs", document.Descendants(Hp + "p").Count());
            result.Add("textRuns", document.Descendants(Hp + "t").Count());
            result.Add("mergedCells", document.Descendants(Hp + "cellSpan").Count(span => GetInt(span, "rowSpan", 1) > 1 || GetInt(span, "colSpan", 1) > 1));
            result.Add("nestedTables", document.Descendants(Hp + "tc").Count(cell => cell.Descendants(Hp + "tbl").Any()));

            foreach (var element in document.Descendants())
            {
                switch (element.Name.LocalName)
                {
                    case "charPr":
                        result.Increment("charStyles");
                        break;
                    case "paraPr":
                        result.Increment("paragraphStyles");
                        break;
                    case "pic":
                        result.Increment("pictures");
                        break;
                    case "img":
                        result.Increment("embeddedImages");
                        break;
                    case "ctrl":
                        result.Increment("controls");
                        break;
                    case "fieldBegin":
                    case "field":
                        result.Increment("fields");
                        break;
                    case "formObject":
                        result.Increment("formObjects");
                        break;
                    case "header":
                        result.Increment("pageHeaders");
                        break;
                    case "footer":
                        result.Increment("pageFooters");
                        break;
                    case "footNote":
                        result.Increment("footnotes");
                        break;
                    case "endNote":
                        result.Increment("endnotes");
                        break;
                    case "equation":
                        result.Increment("equations");
                        break;
                    case "chart":
                        result.Increment("charts");
                        break;
                    case "ole":
                        result.Increment("oleObjects");
                        break;
                    case "video":
                        result.Increment("videos");
                        break;
                    case "line":
                    case "rect":
                    case "ellipse":
                    case "arc":
                    case "polygon":
                    case "curve":
                    case "container":
                    case "textart":
                    case "shapeObject":
                        result.Increment("drawingShapes");
                        break;
                }
            }
        }

        private static bool IsXmlEntry(string path)
        {
            return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".rdf", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(XElement element, string attributeName, int defaultValue)
        {
            var attribute = element.Attribute(attributeName);
            int value;
            return attribute != null && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
        }

        private static string EscapeMarkdown(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        internal sealed class ScanSummary
        {
            public ScanSummary(string inputPath)
            {
                InputPath = inputPath;
                Files = new List<FileScanResult>();
                Aggregate = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            public string InputPath { get; private set; }

            public IList<FileScanResult> Files { get; private set; }

            public IDictionary<string, int> Aggregate { get; private set; }

            public int PackageErrors { get; private set; }

            public int XmlParseErrors { get; private set; }

            public int Count(string name)
            {
                int value;
                return Aggregate.TryGetValue(name, out value) ? value : 0;
            }

            public void Add(FileScanResult result)
            {
                if (result == null)
                {
                    return;
                }

                if (result.PackageError.Length > 0)
                {
                    PackageErrors++;
                }

                XmlParseErrors += result.XmlParseErrors;
                foreach (var item in result.Counts)
                {
                    Aggregate[item.Key] = Count(item.Key) + item.Value;
                }
            }
        }

        internal sealed class FileScanResult
        {
            public FileScanResult(string path)
            {
                Path = path;
                Counts = new Dictionary<string, int>(StringComparer.Ordinal);
                XmlErrors = new List<string>();
                PackageError = string.Empty;
            }

            public string Path { get; private set; }

            public IDictionary<string, int> Counts { get; private set; }

            public int XmlParseErrors { get; set; }

            public IList<string> XmlErrors { get; private set; }

            public string PackageError { get; set; }

            public int Count(string name)
            {
                int value;
                return Counts.TryGetValue(name, out value) ? value : 0;
            }

            public void Increment(string name)
            {
                Add(name, 1);
            }

            public void Add(string name, int value)
            {
                if (value == 0)
                {
                    return;
                }

                Counts[name] = Count(name) + value;
            }
        }
    }
}
