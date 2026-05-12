using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxTableCellBackgroundEditor
    {
        private static readonly XNamespace Hc = "http://www.hancom.co.kr/hwpml/2011/core";
        private static readonly XNamespace Hh = "http://www.hancom.co.kr/hwpml/2011/head";
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableCellBackgroundResult Apply(TableCellBackgroundOptions options)
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

            var clearFill = IsClearColor(options.Color);
            var color = clearFill ? string.Empty : NormalizeColor(options.Color);
            var result = new TableCellBackgroundResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                Row = options.Row,
                Column = options.Column,
                RowSpan = options.RowSpan,
                ColumnSpan = options.ColumnSpan,
                Color = color,
                ClearFill = clearFill,
                Note = "table cell background was not applied"
            };

            if (string.IsNullOrWhiteSpace(options.Color))
            {
                result.Note = "--color is required";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!clearFill && color == null)
            {
                result.Note = "unsupported background color: " + options.Color;
                WriteReport(result, options.ReportPath);
                return result;
            }

            var entries = SimpleZipArchive.ReadAll(result.InputPath);
            byte[] headerBytes;
            if (!entries.TryGetValue("Contents/header.xml", out headerBytes))
            {
                result.Note = "Contents/header.xml was not found";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var header = XDocument.Parse(Encoding.UTF8.GetString(headerBytes), LoadOptions.PreserveWhitespace);
            var borderFills = header.Descendants(Hh + "borderFill")
                .Where(item => GetInt(item, "id", -1) >= 0)
                .ToDictionary(item => GetInt(item, "id", -1), item => item);
            if (borderFills.Count == 0)
            {
                result.Note = "border fill definitions were not found in Contents/header.xml";
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
                result.Note = "background rectangle exceeds table bounds";
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
                result.Note = "no cells intersected the background rectangle";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var previousIds = affected
                .Select(cell => GetIntFromAttribute(cell, "borderFillIDRef", -1))
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            if (previousIds.Any(id => id < 0 || !borderFills.ContainsKey(id)))
            {
                result.Note = "affected cell references a missing borderFillIDRef";
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.PreviousBorderFillIds = string.Join(",", previousIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
            var clonedIdsBySource = new Dictionary<int, int>();
            foreach (var previousId in previousIds)
            {
                clonedIdsBySource[previousId] = CloneBorderFill(header, borderFills[previousId], color, clearFill);
            }

            result.ClonedBorderFillIds = string.Join(",", clonedIdsBySource.Values.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
            result.ClonedBorderFillCount = clonedIdsBySource.Count;
            foreach (var cell in affected)
            {
                var previousId = GetIntFromAttribute(cell, "borderFillIDRef", -1);
                cell.SetAttributeValue("borderFillIDRef", clonedIdsBySource[previousId].ToString(CultureInfo.InvariantCulture));
            }

            var newGrid = HwpxTableModel.BuildGrid(table);
            if (newGrid.RowCount != result.OriginalRows || newGrid.ColumnCount != result.OriginalColumns)
            {
                result.Note = "postcondition failed: background changed table grid size";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (newGrid.OverlapIssues.Count > 0)
            {
                result.Note = "postcondition failed: background created overlapping cell spans";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!AffectedCellsReferenceClones(affected, clonedIdsBySource.Values))
            {
                result.Note = "postcondition failed: not every affected cell received a cloned borderFillIDRef";
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.NewRows = newGrid.RowCount;
            result.NewColumns = newGrid.ColumnCount;
            result.Applied = true;
            result.Note = "table cell background applied";
            entries[sectionPart] = SerializeXml(section);
            entries["Contents/header.xml"] = SerializeXml(header);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static int CloneBorderFill(XDocument header, XElement source, string color, bool clearFill)
        {
            var clone = new XElement(source);
            var nextId = header.Descendants(Hh + "borderFill")
                .Select(item => GetInt(item, "id", -1))
                .DefaultIfEmpty(-1)
                .Max() + 1;
            clone.SetAttributeValue("id", nextId.ToString(CultureInfo.InvariantCulture));
            if (clearFill)
            {
                clone.Elements(Hc + "fillBrush").Remove();
            }
            else
            {
                SetSolidFill(clone, color);
            }

            source.AddAfterSelf(clone);
            var parent = source.Parent;
            var itemCount = GetInt(parent, "itemCnt", -1);
            if (itemCount >= 0)
            {
                parent.SetAttributeValue("itemCnt", (itemCount + 1).ToString(CultureInfo.InvariantCulture));
            }

            return nextId;
        }

        private static void SetSolidFill(XElement borderFill, string color)
        {
            var fillBrush = borderFill.Element(Hc + "fillBrush");
            if (fillBrush == null)
            {
                fillBrush = new XElement(Hc + "fillBrush");
                borderFill.Add(fillBrush);
            }

            foreach (var child in fillBrush.Elements().Where(item => item.Name != Hc + "winBrush").ToList())
            {
                child.Remove();
            }

            var winBrush = fillBrush.Element(Hc + "winBrush");
            if (winBrush == null)
            {
                winBrush = new XElement(Hc + "winBrush");
                fillBrush.Add(winBrush);
            }

            winBrush.SetAttributeValue("faceColor", color);
            if (winBrush.Attribute("hatchColor") == null)
            {
                winBrush.SetAttributeValue("hatchColor", "#FF000000");
            }

            if (winBrush.Attribute("alpha") == null)
            {
                winBrush.SetAttributeValue("alpha", "0");
            }
        }

        private static bool AffectedCellsReferenceClones(IEnumerable<XElement> cells, IEnumerable<int> cloneIds)
        {
            var set = new HashSet<int>(cloneIds);
            return cells.All(cell => set.Contains(GetIntFromAttribute(cell, "borderFillIDRef", -1)));
        }

        private static bool IsClearColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return false;
            }

            var value = color.Trim().ToLowerInvariant();
            return value == "none" || value == "clear" || value == "transparent";
        }

        private static string NormalizeColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return null;
            }

            var value = color.Trim();
            if (!value.StartsWith("#", StringComparison.Ordinal))
            {
                value = "#" + value;
            }

            return Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") ? value.ToUpperInvariant() : null;
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

        private static bool Intersects(int row, int column, int rowSpan, int columnSpan, int selectedRow, int selectedColumn, int selectedRowSpan, int selectedColumnSpan)
        {
            return row < selectedRow + selectedRowSpan &&
                   row + rowSpan > selectedRow &&
                   column < selectedColumn + selectedColumnSpan &&
                   column + columnSpan > selectedColumn;
        }

        private static string FormatAffectedAddresses(IEnumerable<XElement> cells)
        {
            return string.Join(",", cells.Select(cell =>
            {
                var address = cell.Element(Hp + "cellAddr");
                return address == null
                    ? "?:?"
                    : GetInt(address, "rowAddr", -1).ToString(CultureInfo.InvariantCulture) + ":" +
                      GetInt(address, "colAddr", -1).ToString(CultureInfo.InvariantCulture);
            }).OrderBy(value => value, StringComparer.Ordinal).ToArray());
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

        private static int GetIntFromAttribute(XElement element, string attributeName, int defaultValue)
        {
            var attribute = element == null ? null : element.Attribute(attributeName);
            if (attribute == null)
            {
                return defaultValue;
            }

            int value;
            return int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
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

        private static void WriteReport(TableCellBackgroundResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Cell Background Package Operation");
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
            AppendReportRow(report, "color", result.Color);
            AppendReportRow(report, "clear fill", result.ClearFill ? "true" : "false");
            AppendReportRow(report, "previous border fill ids", result.PreviousBorderFillIds);
            AppendReportRow(report, "cloned border fill ids", result.ClonedBorderFillIds);
            AppendReportRow(report, "cloned border fill count", result.ClonedBorderFillCount.ToString(CultureInfo.InvariantCulture));
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

    internal sealed class TableCellBackgroundOptions
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public string Section { get; set; }
        public int TableIndex { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public int RowSpan { get; set; }
        public int ColumnSpan { get; set; }
        public string Color { get; set; }
        public string ReportPath { get; set; }
    }

    internal sealed class TableCellBackgroundResult
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
        public string Color { get; set; }
        public bool ClearFill { get; set; }
        public string PreviousBorderFillIds { get; set; }
        public string ClonedBorderFillIds { get; set; }
        public int ClonedBorderFillCount { get; set; }
        public int AffectedCells { get; set; }
        public string AffectedCellAddresses { get; set; }
        public int OriginalRows { get; set; }
        public int NewRows { get; set; }
        public int OriginalColumns { get; set; }
        public int NewColumns { get; set; }
        public string Note { get; set; }
    }
}
