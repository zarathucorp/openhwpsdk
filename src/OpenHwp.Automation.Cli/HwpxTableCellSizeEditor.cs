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
    internal static class HwpxTableCellSizeEditor
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableCellSizeResult Apply(TableCellSizeOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.InputPath) || string.IsNullOrWhiteSpace(options.OutputPath))
            {
                throw new ArgumentException("input and output paths are required.", nameof(options));
            }

            if (options.TableIndex < 0)
            {
                throw new ArgumentException("table index must be zero or greater.", nameof(options));
            }

            var result = new TableCellSizeResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                EqualizeWidths = options.EqualizeWidths,
                EqualizeHeights = options.EqualizeHeights,
                Note = "table cell size operation was not applied"
            };

            if (!result.EqualizeWidths && !result.EqualizeHeights)
            {
                result.Note = "at least one of --equalize-widths or --equalize-heights is required";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!string.Equals(result.Section, "section0", StringComparison.OrdinalIgnoreCase))
            {
                result.Note = "table cell size package operation currently supports section0 only";
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
            result.PhysicalCells = grid.Cells.Count;
            if (!IsSimpleEditableTable(table, grid, out var rejectionReason))
            {
                result.Note = rejectionReason;
                WriteReport(result, options.ReportPath);
                return result;
            }

            var rows = table.Elements(Hp + "tr").ToList();
            var originalColumnWidths = ColumnWidthsFromFirstRow(rows);
            var originalRowHeights = RowHeights(rows);
            result.OriginalTableWidth = originalColumnWidths.Sum();
            result.OriginalTableHeight = originalRowHeights.Sum();
            result.OriginalColumnWidths = FormatSizes(originalColumnWidths);
            result.OriginalRowHeights = FormatSizes(originalRowHeights);

            if (result.EqualizeWidths)
            {
                if (!TryDistribute(result.OriginalTableWidth, result.OriginalColumns, out var newColumnWidths))
                {
                    result.Note = "table width cannot be evenly distributed across columns";
                    WriteReport(result, options.ReportPath);
                    return result;
                }

                result.NewColumnWidths = FormatSizes(newColumnWidths);
                result.WidthCellsChanged = ApplyColumnWidths(rows, newColumnWidths);
            }
            else
            {
                result.NewColumnWidths = result.OriginalColumnWidths;
            }

            if (result.EqualizeHeights)
            {
                if (!TryDistribute(result.OriginalTableHeight, result.OriginalRows, out var newRowHeights))
                {
                    result.Note = "table height cannot be evenly distributed across rows";
                    WriteReport(result, options.ReportPath);
                    return result;
                }

                result.NewRowHeights = FormatSizes(newRowHeights);
                result.HeightCellsChanged = ApplyRowHeights(rows, newRowHeights);
            }
            else
            {
                result.NewRowHeights = result.OriginalRowHeights;
            }

            UpdateTableSize(table);
            var newGrid = HwpxTableModel.BuildGrid(table);
            result.NewRows = newGrid.RowCount;
            result.NewColumns = newGrid.ColumnCount;
            result.NewTableWidth = TableWidth(table);
            result.NewTableHeight = TableHeight(table);
            if (!ValidatePostconditions(result, newGrid, out var postconditionReason))
            {
                result.Note = postconditionReason;
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.Applied = true;
            result.Note = "table cell size operation applied";
            entries[sectionPart] = SerializeXml(section);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static bool IsSimpleEditableTable(XElement table, TableGrid grid, out string reason)
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

            if (HwpxTableModel.HasMergedCells(table))
            {
                reason = "table cell size package operation does not support merged cells yet";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(cell => cell.Descendants(Hp + "tbl").Any()))
            {
                reason = "table cell size package operation does not support nested tables yet";
                return false;
            }

            if (GetInt(table, "rowCnt", grid.RowCount) != grid.RowCount ||
                GetInt(table, "colCnt", grid.ColumnCount) != grid.ColumnCount)
            {
                reason = "table declared rowCnt/colCnt does not match observed grid";
                return false;
            }

            var rows = table.Elements(Hp + "tr").ToList();
            if (rows.Count != grid.RowCount)
            {
                reason = "table physical row count does not match observed grid";
                return false;
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var cells = rows[rowIndex].Elements(Hp + "tc").ToList();
                if (cells.Count != grid.ColumnCount)
                {
                    reason = "table row has uneven cell count";
                    return false;
                }

                for (var columnIndex = 0; columnIndex < cells.Count; columnIndex++)
                {
                    var cell = cells[columnIndex];
                    var address = cell.Element(Hp + "cellAddr");
                    var span = cell.Element(Hp + "cellSpan");
                    var size = cell.Element(Hp + "cellSz");
                    if (address == null ||
                        GetInt(address, "rowAddr", -1) != rowIndex ||
                        GetInt(address, "colAddr", -1) != columnIndex ||
                        span == null ||
                        GetInt(span, "rowSpan", 1) != 1 ||
                        GetInt(span, "colSpan", 1) != 1)
                    {
                        reason = "table cell addresses are not contiguous physical row/column order";
                        return false;
                    }

                    if (size == null || GetInt(size, "width", 0) <= 0 || GetInt(size, "height", 0) <= 0)
                    {
                        reason = "table cell is missing positive cellSz width or height";
                        return false;
                    }
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidatePostconditions(TableCellSizeResult result, TableGrid newGrid, out string reason)
        {
            if (newGrid.RowCount != result.OriginalRows || newGrid.ColumnCount != result.OriginalColumns)
            {
                reason = "postcondition failed: table grid size changed";
                return false;
            }

            if (newGrid.OverlapIssues.Count > 0)
            {
                reason = "postcondition failed: size operation created overlapping cell spans";
                return false;
            }

            if (result.EqualizeWidths && result.NewTableWidth != result.OriginalTableWidth)
            {
                reason = "postcondition failed: table width changed " +
                         result.OriginalTableWidth.ToString(CultureInfo.InvariantCulture) + "->" +
                         result.NewTableWidth.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (result.EqualizeHeights && result.NewTableHeight != result.OriginalTableHeight)
            {
                reason = "postcondition failed: table height changed " +
                         result.OriginalTableHeight.ToString(CultureInfo.InvariantCulture) + "->" +
                         result.NewTableHeight.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static IList<int> ColumnWidthsFromFirstRow(IList<XElement> rows)
        {
            var firstRow = rows == null ? null : rows.FirstOrDefault();
            return firstRow == null
                ? new List<int>()
                : firstRow.Elements(Hp + "tc").Select(GetCellWidth).ToList();
        }

        private static IList<int> RowHeights(IList<XElement> rows)
        {
            return rows == null
                ? new List<int>()
                : rows.Select(row => row.Elements(Hp + "tc").Select(GetCellHeight).DefaultIfEmpty(0).Max()).ToList();
        }

        private static bool TryDistribute(int total, int count, out IList<int> values)
        {
            values = new List<int>();
            if (total <= 0 || count <= 0 || total < count)
            {
                return false;
            }

            var baseValue = total / count;
            var remainder = total - (baseValue * count);
            for (var index = 0; index < count; index++)
            {
                values.Add(baseValue + (index == count - 1 ? remainder : 0));
            }

            return values.All(value => value > 0);
        }

        private static int ApplyColumnWidths(IList<XElement> rows, IList<int> widths)
        {
            var changed = 0;
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var cells = rows[rowIndex].Elements(Hp + "tc").ToList();
                for (var columnIndex = 0; columnIndex < cells.Count; columnIndex++)
                {
                    var size = EnsureCellChild(cells[columnIndex], "cellSz");
                    var newWidth = widths[columnIndex];
                    if (GetInt(size, "width", 0) != newWidth)
                    {
                        changed++;
                    }

                    size.SetAttributeValue("width", newWidth.ToString(CultureInfo.InvariantCulture));
                }
            }

            return changed;
        }

        private static int ApplyRowHeights(IList<XElement> rows, IList<int> heights)
        {
            var changed = 0;
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var newHeight = heights[rowIndex];
                var cells = rows[rowIndex].Elements(Hp + "tc").ToList();
                foreach (var cell in cells)
                {
                    var size = EnsureCellChild(cell, "cellSz");
                    if (GetInt(size, "height", 0) != newHeight)
                    {
                        changed++;
                    }

                    size.SetAttributeValue("height", newHeight.ToString(CultureInfo.InvariantCulture));
                }
            }

            return changed;
        }

        private static void UpdateTableSize(XElement table)
        {
            var size = table == null ? null : table.Element(Hp + "sz");
            if (size == null)
            {
                return;
            }

            var width = TableWidth(table);
            var height = TableHeight(table);
            if (width > 0)
            {
                size.SetAttributeValue("width", width.ToString(CultureInfo.InvariantCulture));
            }

            if (height > 0)
            {
                size.SetAttributeValue("height", height.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static int TableWidth(XElement table)
        {
            var firstRow = table == null ? null : table.Elements(Hp + "tr").FirstOrDefault();
            return firstRow == null ? 0 : firstRow.Elements(Hp + "tc").Select(GetCellWidth).Sum();
        }

        private static int TableHeight(XElement table)
        {
            var rows = table == null ? new List<XElement>() : table.Elements(Hp + "tr").ToList();
            return rows.Select(row => row.Elements(Hp + "tc").Select(GetCellHeight).DefaultIfEmpty(0).Max()).Sum();
        }

        private static int GetCellWidth(XElement cell)
        {
            var size = cell == null ? null : cell.Element(Hp + "cellSz");
            return size == null ? 0 : GetInt(size, "width", 0);
        }

        private static int GetCellHeight(XElement cell)
        {
            var size = cell == null ? null : cell.Element(Hp + "cellSz");
            return size == null ? 0 : GetInt(size, "height", 0);
        }

        private static XElement EnsureCellChild(XElement cell, string localName)
        {
            var child = cell.Element(Hp + localName);
            if (child != null)
            {
                return child;
            }

            child = new XElement(Hp + localName);
            cell.Add(child);
            return child;
        }

        private static string FormatSizes(IEnumerable<int> sizes)
        {
            return string.Join(",", (sizes ?? new List<int>()).Select(size => size.ToString(CultureInfo.InvariantCulture)).ToArray());
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

        private static void WriteReport(TableCellSizeResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Cell Size Package Operation");
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
            AppendReportRow(report, "equalize widths", result.EqualizeWidths ? "true" : "false");
            AppendReportRow(report, "equalize heights", result.EqualizeHeights ? "true" : "false");
            AppendReportRow(report, "original rows", result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new rows", result.NewRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original columns", result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new columns", result.NewColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "physical cells", result.PhysicalCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original table width", result.OriginalTableWidth.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new table width", result.NewTableWidth.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original table height", result.OriginalTableHeight.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new table height", result.NewTableHeight.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original column widths", result.OriginalColumnWidths);
            AppendReportRow(report, "new column widths", result.NewColumnWidths);
            AppendReportRow(report, "original row heights", result.OriginalRowHeights);
            AppendReportRow(report, "new row heights", result.NewRowHeights);
            AppendReportRow(report, "width cells changed", result.WidthCellsChanged.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "height cells changed", result.HeightCellsChanged.ToString(CultureInfo.InvariantCulture));
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

    internal sealed class TableCellSizeOptions
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public string Section { get; set; }
        public int TableIndex { get; set; }
        public bool EqualizeWidths { get; set; }
        public bool EqualizeHeights { get; set; }
        public string ReportPath { get; set; }
    }

    internal sealed class TableCellSizeResult
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public string Section { get; set; }
        public bool Applied { get; set; }
        public int TableIndex { get; set; }
        public int TopLevelTableCount { get; set; }
        public bool EqualizeWidths { get; set; }
        public bool EqualizeHeights { get; set; }
        public int OriginalRows { get; set; }
        public int NewRows { get; set; }
        public int OriginalColumns { get; set; }
        public int NewColumns { get; set; }
        public int PhysicalCells { get; set; }
        public int OriginalTableWidth { get; set; }
        public int NewTableWidth { get; set; }
        public int OriginalTableHeight { get; set; }
        public int NewTableHeight { get; set; }
        public string OriginalColumnWidths { get; set; }
        public string NewColumnWidths { get; set; }
        public string OriginalRowHeights { get; set; }
        public string NewRowHeights { get; set; }
        public int WidthCellsChanged { get; set; }
        public int HeightCellsChanged { get; set; }
        public string Note { get; set; }
    }
}
