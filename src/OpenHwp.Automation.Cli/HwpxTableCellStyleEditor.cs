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
    internal static class HwpxTableCellStyleEditor
    {
        private static readonly XNamespace Hh = "http://www.hancom.co.kr/hwpml/2011/head";
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableCellStyleResult Apply(TableCellStyleOptions options)
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

            var result = new TableCellStyleResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                Row = options.Row,
                Column = options.Column,
                RowSpan = options.RowSpan,
                ColumnSpan = options.ColumnSpan,
                BorderFillId = options.BorderFillId,
                Note = "table cell style was not applied"
            };

            if (!options.BorderFillId.HasValue)
            {
                result.Note = "--border-fill-id is required";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!string.Equals(result.Section, "section0", StringComparison.OrdinalIgnoreCase))
            {
                result.Note = "table cell style package operation currently supports section0 only";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var entries = SimpleZipArchive.ReadAll(result.InputPath);
            var borderFillIds = ReadBorderFillIds(entries);
            if (borderFillIds.Count == 0)
            {
                result.Note = "border fill definitions were not found in Contents/header.xml";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!borderFillIds.Contains(options.BorderFillId.Value))
            {
                result.Note = "border fill id was not found: " + options.BorderFillId.Value.ToString(CultureInfo.InvariantCulture);
                WriteReport(result, options.ReportPath);
                return result;
            }

            var sectionPart = "Contents/" + result.Section + ".xml";
            if (!entries.ContainsKey(sectionPart))
            {
                result.Note = "section part was not found: " + sectionPart;
                WriteReport(result, options.ReportPath);
                return result;
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
                result.Note = "style rectangle exceeds table bounds";
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
            if (affected.Count == 0)
            {
                result.Note = "no cells intersected the style rectangle";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var previousValues = new List<string>();
            foreach (var cell in affected)
            {
                previousValues.Add(((string)cell.Attribute("borderFillIDRef")) ?? string.Empty);
                cell.SetAttributeValue("borderFillIDRef", options.BorderFillId.Value.ToString(CultureInfo.InvariantCulture));
            }

            result.PreviousBorderFillIds = string.Join(",", previousValues.Distinct().OrderBy(value => value, StringComparer.Ordinal).ToArray());
            result.AffectedCellAddresses = FormatAffectedAddresses(affected);

            var newGrid = HwpxTableModel.BuildGrid(table);
            if (newGrid.RowCount != result.OriginalRows || newGrid.ColumnCount != result.OriginalColumns)
            {
                result.Note = "postcondition failed: style change modified table grid size";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (newGrid.OverlapIssues.Count > 0)
            {
                result.Note = "postcondition failed: style change created overlapping cell spans";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (affected.Any(cell => !string.Equals((string)cell.Attribute("borderFillIDRef"), options.BorderFillId.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))
            {
                result.Note = "postcondition failed: not every affected cell received borderFillIDRef";
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.NewRows = newGrid.RowCount;
            result.NewColumns = newGrid.ColumnCount;
            result.Applied = true;
            result.Note = "table cell style applied";

            entries[sectionPart] = SerializeXml(section);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
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

        private static ISet<int> ReadBorderFillIds(IDictionary<string, byte[]> entries)
        {
            var result = new HashSet<int>();
            byte[] bytes;
            if (entries == null || !entries.TryGetValue("Contents/header.xml", out bytes))
            {
                return result;
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(Encoding.UTF8.GetString(bytes), LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                return result;
            }

            foreach (var borderFill in document.Descendants(Hh + "borderFill"))
            {
                var id = GetInt(borderFill, "id", -1);
                if (id >= 0)
                {
                    result.Add(id);
                }
            }

            return result;
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

        private static void WriteReport(TableCellStyleResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Cell Style Package Operation");
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
            AppendReportRow(report, "border fill id", result.BorderFillId.HasValue ? result.BorderFillId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendReportRow(report, "previous border fill ids", result.PreviousBorderFillIds);
            AppendReportRow(report, "affected cells", result.AffectedCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "affected cell addresses", result.AffectedCellAddresses);
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

    internal sealed class TableCellStyleOptions
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public int TableIndex { get; set; }

        public int Row { get; set; }

        public int Column { get; set; }

        public int RowSpan { get; set; }

        public int ColumnSpan { get; set; }

        public int? BorderFillId { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class TableCellStyleResult
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

        public int? BorderFillId { get; set; }

        public int AffectedCells { get; set; }

        public string PreviousBorderFillIds { get; set; }

        public string AffectedCellAddresses { get; set; }

        public int OriginalRows { get; set; }

        public int NewRows { get; set; }

        public int OriginalColumns { get; set; }

        public int NewColumns { get; set; }

        public string Note { get; set; }
    }
}
