using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxTableCellAlignEditor
    {
        private static readonly XNamespace Hh = "http://www.hancom.co.kr/hwpml/2011/head";
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableCellAlignResult Apply(TableCellAlignOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.InputPath) || string.IsNullOrWhiteSpace(options.OutputPath))
            {
                throw new ArgumentException("input and output paths are required.", nameof(options));
            }

            if (options.TableIndex < 0 || options.Row < 0 || options.Column < 0)
            {
                throw new ArgumentException("table index, row, and column must be zero or greater.", nameof(options));
            }

            if (options.RowSpan <= 0)
            {
                options.RowSpan = 1;
            }

            if (options.ColumnSpan <= 0)
            {
                options.ColumnSpan = 1;
            }

            var horizontal = NormalizeHorizontal(options.Horizontal);
            var vertical = NormalizeVertical(options.Vertical);
            var result = new TableCellAlignResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                Row = options.Row,
                Column = options.Column,
                RowSpan = options.RowSpan,
                ColumnSpan = options.ColumnSpan,
                Horizontal = horizontal,
                Vertical = vertical,
                Note = "table cell alignment was not applied"
            };

            if (options.Horizontal != null && horizontal == null)
            {
                result.Note = "unsupported horizontal alignment: " + options.Horizontal;
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (options.Vertical != null && vertical == null)
            {
                result.Note = "unsupported vertical alignment: " + options.Vertical;
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (horizontal == null && vertical == null)
            {
                result.Note = "--horizontal or --vertical is required";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!string.Equals(result.Section, "section0", StringComparison.OrdinalIgnoreCase))
            {
                result.Note = "table cell alignment package operation currently supports section0 only";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var entries = SimpleZipArchive.ReadAll(result.InputPath);
            var sectionPart = "Contents/" + result.Section + ".xml";
            if (!entries.ContainsKey(sectionPart))
            {
                result.Note = "section part was not found: " + sectionPart;
                WriteReport(result, options.ReportPath);
                return result;
            }

            XDocument header = null;
            if (horizontal != null)
            {
                byte[] headerBytes;
                if (!entries.TryGetValue("Contents/header.xml", out headerBytes))
                {
                    result.Note = "Contents/header.xml was not found";
                    WriteReport(result, options.ReportPath);
                    return result;
                }

                header = XDocument.Parse(Encoding.UTF8.GetString(headerBytes), LoadOptions.PreserveWhitespace);
            }

            var section = XDocument.Parse(Encoding.UTF8.GetString(entries[sectionPart]), LoadOptions.PreserveWhitespace);
            var tables = section.Descendants(Hp + "tbl")
                .Where(table => !table.Ancestors(Hp + "tbl").Any())
                .ToList();
            result.TopLevelTableCount = tables.Count;
            if (options.TableIndex >= tables.Count)
            {
                result.Note = "table index is out of range";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var table = tables[options.TableIndex];
            var grid = HwpxTableModel.BuildGrid(table);
            result.OriginalRows = grid.RowCount;
            result.OriginalColumns = grid.ColumnCount;
            result.NewRows = result.OriginalRows;
            result.NewColumns = result.OriginalColumns;
            if (!ValidateGrid(table, grid, out var rejectionReason))
            {
                result.Note = rejectionReason;
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (options.Row + options.RowSpan > grid.RowCount ||
                options.Column + options.ColumnSpan > grid.ColumnCount)
            {
                result.Note = "alignment rectangle exceeds table bounds";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var affected = grid.Cells
                .Where(cell => Intersects(
                    cell.Row,
                    cell.Column,
                    cell.RowSpan,
                    cell.ColumnSpan,
                    options.Row,
                    options.Column,
                    options.RowSpan,
                    options.ColumnSpan))
                .Select(cell => cell.Element)
                .Distinct()
                .ToList();
            result.AffectedCells = affected.Count;
            result.AffectedCellAddresses = FormatAffectedAddresses(affected);
            if (affected.Count == 0)
            {
                result.Note = "no cells intersected the alignment rectangle";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var paragraphStyleMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var cell in affected)
            {
                if (vertical != null)
                {
                    var subList = cell.Element(Hp + "subList");
                    if (subList == null)
                    {
                        result.Note = "target cell has no subList for vertical alignment";
                        WriteReport(result, options.ReportPath);
                        return result;
                    }

                    subList.SetAttributeValue("vertAlign", vertical);
                    result.VerticalCells++;
                }

                if (horizontal != null)
                {
                    foreach (var paragraph in HwpxTableModel.GetDirectCellParagraphs(cell))
                    {
                        var currentParaPrId = ((string)paragraph.Attribute("paraPrIDRef")) ?? string.Empty;
                        if (currentParaPrId.Length == 0)
                        {
                            result.Note = "target paragraph has no paraPrIDRef";
                            WriteReport(result, options.ReportPath);
                            return result;
                        }

                        string newParaPrId;
                        if (!paragraphStyleMap.TryGetValue(currentParaPrId, out newParaPrId))
                        {
                            if (!CloneParagraphStyle(header, currentParaPrId, horizontal, out newParaPrId, out var cloneNote))
                            {
                                result.Note = cloneNote;
                                WriteReport(result, options.ReportPath);
                                return result;
                            }

                            paragraphStyleMap[currentParaPrId] = newParaPrId;
                            result.ClonedParaPrCount++;
                        }

                        paragraph.SetAttributeValue("paraPrIDRef", newParaPrId);
                        result.HorizontalParagraphs++;
                    }
                }
            }

            var newGrid = HwpxTableModel.BuildGrid(table);
            if (newGrid.RowCount != result.OriginalRows || newGrid.ColumnCount != result.OriginalColumns)
            {
                result.Note = "postcondition failed: alignment changed table grid size";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (newGrid.OverlapIssues.Count > 0)
            {
                result.Note = "postcondition failed: alignment created overlapping cell spans";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (horizontal != null && !AllDirectParagraphsHaveHorizontal(header, affected, horizontal))
            {
                result.Note = "postcondition failed: not every affected paragraph has requested horizontal alignment";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (vertical != null && affected.Any(cell => !string.Equals((string)cell.Element(Hp + "subList").Attribute("vertAlign"), vertical, StringComparison.Ordinal)))
            {
                result.Note = "postcondition failed: not every affected cell has requested vertical alignment";
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.NewRows = newGrid.RowCount;
            result.NewColumns = newGrid.ColumnCount;
            result.Applied = true;
            result.Note = "table cell alignment applied";

            entries[sectionPart] = SerializeXml(section);
            if (header != null)
            {
                entries["Contents/header.xml"] = SerializeXml(header);
            }

            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static bool CloneParagraphStyle(XDocument header, string paraPrId, string horizontal, out string newParaPrId, out string note)
        {
            newParaPrId = string.Empty;
            if (header == null || header.Root == null)
            {
                note = "header document is empty";
                return false;
            }

            var original = header.Descendants(Hh + "paraPr")
                .FirstOrDefault(item => string.Equals((string)item.Attribute("id"), paraPrId, StringComparison.Ordinal));
            if (original == null)
            {
                note = "paraPr was not found in header.xml: " + paraPrId;
                return false;
            }

            var parent = original.Parent;
            if (parent == null)
            {
                note = "paraPr has no parent in header.xml: " + paraPrId;
                return false;
            }

            var clone = new XElement(original);
            var nextId = header.Descendants(Hh + "paraPr")
                .Select(item => GetInt(item, "id", -1))
                .DefaultIfEmpty(-1)
                .Max() + 1;
            clone.SetAttributeValue("id", nextId.ToString(CultureInfo.InvariantCulture));
            var align = clone.Element(Hh + "align");
            if (align == null)
            {
                align = new XElement(Hh + "align");
                clone.AddFirst(align);
            }

            align.SetAttributeValue("horizontal", horizontal);
            original.AddAfterSelf(clone);
            var itemCount = GetInt(parent, "itemCnt", -1);
            if (itemCount >= 0)
            {
                parent.SetAttributeValue("itemCnt", (itemCount + 1).ToString(CultureInfo.InvariantCulture));
            }

            newParaPrId = nextId.ToString(CultureInfo.InvariantCulture);
            note = string.Empty;
            return true;
        }

        private static bool AllDirectParagraphsHaveHorizontal(XDocument header, IList<XElement> cells, string horizontal)
        {
            var alignById = header.Descendants(Hh + "paraPr")
                .ToDictionary(item => ((string)item.Attribute("id")) ?? string.Empty, item =>
                {
                    var align = item.Element(Hh + "align");
                    return align == null ? string.Empty : ((string)align.Attribute("horizontal")) ?? string.Empty;
                }, StringComparer.Ordinal);
            foreach (var paragraph in cells.SelectMany(HwpxTableModel.GetDirectCellParagraphs))
            {
                var id = ((string)paragraph.Attribute("paraPrIDRef")) ?? string.Empty;
                string value;
                if (!alignById.TryGetValue(id, out value) || !string.Equals(value, horizontal, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeHorizontal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "left":
                    return "LEFT";
                case "center":
                case "centre":
                    return "CENTER";
                case "right":
                    return "RIGHT";
                case "justify":
                    return "JUSTIFY";
                default:
                    return null;
            }
        }

        private static string NormalizeVertical(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "top":
                    return "TOP";
                case "center":
                case "centre":
                case "middle":
                    return "CENTER";
                case "bottom":
                    return "BOTTOM";
                default:
                    return null;
            }
        }

        private static bool ValidateGrid(XElement table, TableGrid grid, out string reason)
        {
            if (grid == null || grid.RowCount <= 0 || grid.ColumnCount <= 0)
            {
                reason = "table grid is empty";
                return false;
            }

            if (grid.OverlapIssues.Count > 0)
            {
                reason = "table grid has overlap issues";
                return false;
            }

            if (GetInt(table, "rowCnt", grid.RowCount) != grid.RowCount ||
                GetInt(table, "colCnt", grid.ColumnCount) != grid.ColumnCount)
            {
                reason = "table declared rowCnt/colCnt does not match observed grid";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool Intersects(
            int row,
            int column,
            int rowSpan,
            int columnSpan,
            int selectedRow,
            int selectedColumn,
            int selectedRowSpan,
            int selectedColumnSpan)
        {
            return row < selectedRow + selectedRowSpan &&
                   row + rowSpan > selectedRow &&
                   column < selectedColumn + selectedColumnSpan &&
                   column + columnSpan > selectedColumn;
        }

        private static string FormatAffectedAddresses(IEnumerable<XElement> cells)
        {
            return string.Join(",", cells
                .Select(cell =>
                {
                    var address = cell.Element(Hp + "cellAddr");
                    return address == null
                        ? "?:?"
                        : GetInt(address, "rowAddr", -1).ToString(CultureInfo.InvariantCulture) + ":" +
                          GetInt(address, "colAddr", -1).ToString(CultureInfo.InvariantCulture);
                })
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
        }

        private static string NormalizeSection(string section)
        {
            var value = string.IsNullOrWhiteSpace(section) ? "section0" : section.Trim();
            if (value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                value = Path.GetFileNameWithoutExtension(value);
            }

            return value;
        }

        private static int GetInt(XElement element, string attributeName, int defaultValue)
        {
            var attribute = element == null ? null : element.Attribute(attributeName);
            if (attribute == null)
            {
                return defaultValue;
            }

            int value;
            return int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
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

        private static void WriteReport(TableCellAlignResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Cell Alignment Package Operation");
            report.AppendLine();
            report.AppendLine("- input: `" + result.InputPath + "`");
            report.AppendLine("- output: `" + result.OutputPath + "`");
            report.AppendLine("- section: `" + result.Section + "`");
            report.AppendLine("- verdict: " + (result.Applied ? "applied" : "failed"));
            report.AppendLine();
            report.AppendLine("| field | value |");
            report.AppendLine("| --- | --- |");
            AppendReportRow(report, "table index", result.TableIndex.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "top-level table count", result.TopLevelTableCount.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "row", result.Row.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "column", result.Column.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "row span", result.RowSpan.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "column span", result.ColumnSpan.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "horizontal", result.Horizontal);
            AppendReportRow(report, "vertical", result.Vertical);
            AppendReportRow(report, "affected cells", result.AffectedCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "affected cell addresses", result.AffectedCellAddresses);
            AppendReportRow(report, "horizontal paragraphs", result.HorizontalParagraphs.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "vertical cells", result.VerticalCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "cloned paraPr count", result.ClonedParaPrCount.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original rows", result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new rows", result.NewRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original columns", result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new columns", result.NewColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "note", result.Note);

            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));
        }

        private static void AppendReportRow(StringBuilder report, string field, string value)
        {
            report.Append("| ");
            report.Append((field ?? string.Empty).Replace("|", "\\|"));
            report.Append(" | ");
            report.Append((value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " "));
            report.AppendLine(" |");
        }
    }

    internal sealed class TableCellAlignOptions
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public int TableIndex { get; set; }

        public int Row { get; set; }

        public int Column { get; set; }

        public int RowSpan { get; set; }

        public int ColumnSpan { get; set; }

        public string Horizontal { get; set; }

        public string Vertical { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class TableCellAlignResult
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public bool Applied { get; set; }

        public int TableIndex { get; set; }

        public int TopLevelTableCount { get; set; }

        public int Row { get; set; }

        public int Column { get; set; }

        public int RowSpan { get; set; }

        public int ColumnSpan { get; set; }

        public string Horizontal { get; set; }

        public string Vertical { get; set; }

        public int AffectedCells { get; set; }

        public string AffectedCellAddresses { get; set; }

        public int HorizontalParagraphs { get; set; }

        public int VerticalCells { get; set; }

        public int ClonedParaPrCount { get; set; }

        public int OriginalRows { get; set; }

        public int NewRows { get; set; }

        public int OriginalColumns { get; set; }

        public int NewColumns { get; set; }

        public string Note { get; set; }
    }
}
