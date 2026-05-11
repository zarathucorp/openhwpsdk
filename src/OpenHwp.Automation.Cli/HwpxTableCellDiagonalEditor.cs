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
    internal static class HwpxTableCellDiagonalEditor
    {
        private static readonly XNamespace Hh = "http://www.hancom.co.kr/hwpml/2011/head";
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableCellDiagonalResult Apply(TableCellDiagonalOptions options)
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

            var direction = NormalizeDirection(options.Direction);
            var result = new TableCellDiagonalResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                Row = options.Row,
                Column = options.Column,
                RowSpan = options.RowSpan,
                ColumnSpan = options.ColumnSpan,
                Direction = direction,
                Width = string.IsNullOrWhiteSpace(options.Width) ? string.Empty : options.Width.Trim(),
                Color = string.IsNullOrWhiteSpace(options.Color) ? string.Empty : options.Color.Trim(),
                BaseBorderFillId = options.BaseBorderFillId,
                Note = "table cell diagonal was not applied"
            };

            if (options.Direction != null && direction == null)
            {
                result.Note = "unsupported diagonal direction: " + options.Direction;
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (direction == null)
            {
                result.Note = "--direction is required";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!string.Equals(result.Section, "section0", StringComparison.OrdinalIgnoreCase))
            {
                result.Note = "table cell diagonal package operation currently supports section0 only";
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

            if (options.BaseBorderFillId.HasValue && !borderFills.ContainsKey(options.BaseBorderFillId.Value))
            {
                result.Note = "base border fill id was not found: " + options.BaseBorderFillId.Value.ToString(CultureInfo.InvariantCulture);
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
                result.Note = "diagonal rectangle exceeds table bounds";
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
                result.Note = "no cells intersected the diagonal rectangle";
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
            if (options.BaseBorderFillId.HasValue)
            {
                var cloneId = CloneBorderFill(header, borderFills[options.BaseBorderFillId.Value], direction, result.Width, result.Color);
                foreach (var previousId in previousIds)
                {
                    clonedIdsBySource[previousId] = cloneId;
                }

                result.ClonedBorderFillIds = cloneId.ToString(CultureInfo.InvariantCulture);
                result.ClonedBorderFillCount = 1;
            }
            else
            {
                foreach (var previousId in previousIds)
                {
                    clonedIdsBySource[previousId] = CloneBorderFill(header, borderFills[previousId], direction, result.Width, result.Color);
                }

                result.ClonedBorderFillIds = string.Join(",", clonedIdsBySource.Values.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
                result.ClonedBorderFillCount = clonedIdsBySource.Count;
            }

            foreach (var cell in affected)
            {
                var previousId = GetIntFromAttribute(cell, "borderFillIDRef", -1);
                cell.SetAttributeValue("borderFillIDRef", clonedIdsBySource[previousId].ToString(CultureInfo.InvariantCulture));
            }

            var newGrid = HwpxTableModel.BuildGrid(table);
            if (newGrid.RowCount != result.OriginalRows || newGrid.ColumnCount != result.OriginalColumns)
            {
                result.Note = "postcondition failed: diagonal changed table grid size";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (newGrid.OverlapIssues.Count > 0)
            {
                result.Note = "postcondition failed: diagonal created overlapping cell spans";
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
            result.Note = "table cell diagonal applied";

            entries[sectionPart] = SerializeXml(section);
            entries["Contents/header.xml"] = SerializeXml(header);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static int CloneBorderFill(XDocument header, XElement source, string direction, string width, string color)
        {
            var clone = new XElement(source);
            var nextId = header.Descendants(Hh + "borderFill")
                .Select(item => GetInt(item, "id", -1))
                .DefaultIfEmpty(-1)
                .Max() + 1;
            clone.SetAttributeValue("id", nextId.ToString(CultureInfo.InvariantCulture));
            SetSlashType(clone, "slash", direction == "slash" || direction == "both" ? "CENTER" : "NONE");
            SetSlashType(clone, "backSlash", direction == "backslash" || direction == "both" ? "CENTER" : "NONE");
            var diagonal = clone.Element(Hh + "diagonal");
            if (diagonal == null)
            {
                diagonal = new XElement(Hh + "diagonal");
                clone.Add(diagonal);
            }

            diagonal.SetAttributeValue("type", direction == "none" ? "NONE" : "SOLID");
            diagonal.SetAttributeValue("width", string.IsNullOrWhiteSpace(width) ? ExistingOrDefault(diagonal, "width", "0.12 mm") : width);
            diagonal.SetAttributeValue("color", string.IsNullOrWhiteSpace(color) ? ExistingOrDefault(diagonal, "color", "#000000") : color);
            source.AddAfterSelf(clone);
            var parent = source.Parent;
            var itemCount = GetInt(parent, "itemCnt", -1);
            if (itemCount >= 0)
            {
                parent.SetAttributeValue("itemCnt", (itemCount + 1).ToString(CultureInfo.InvariantCulture));
            }

            return nextId;
        }

        private static void SetSlashType(XElement borderFill, string localName, string type)
        {
            var element = borderFill.Element(Hh + localName);
            if (element == null)
            {
                element = new XElement(Hh + localName, new XAttribute("Crooked", "0"), new XAttribute("isCounter", "0"));
                borderFill.AddFirst(element);
            }

            element.SetAttributeValue("type", type);
            if (element.Attribute("Crooked") == null)
            {
                element.SetAttributeValue("Crooked", "0");
            }

            if (element.Attribute("isCounter") == null)
            {
                element.SetAttributeValue("isCounter", "0");
            }
        }

        private static bool AffectedCellsReferenceClones(IEnumerable<XElement> cells, IEnumerable<int> cloneIds)
        {
            var set = new HashSet<int>(cloneIds);
            return cells.All(cell => set.Contains(GetIntFromAttribute(cell, "borderFillIDRef", -1)));
        }

        private static string NormalizeDirection(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "slash":
                case "/":
                    return "slash";
                case "backslash":
                case "back-slash":
                case "\\":
                    return "backslash";
                case "both":
                    return "both";
                case "none":
                case "clear":
                    return "none";
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

        private static string ExistingOrDefault(XElement element, string attributeName, string defaultValue)
        {
            var attribute = element == null ? null : element.Attribute(attributeName);
            return attribute == null || string.IsNullOrWhiteSpace(attribute.Value) ? defaultValue : attribute.Value;
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

        private static void WriteReport(TableCellDiagonalResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Cell Diagonal Package Operation");
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
            AppendReportRow(report, "direction", result.Direction);
            AppendReportRow(report, "width", result.Width);
            AppendReportRow(report, "color", result.Color);
            AppendReportRow(report, "base border fill id", result.BaseBorderFillId.HasValue ? result.BaseBorderFillId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
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

    internal sealed class TableCellDiagonalOptions
    {
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public string Section { get; set; }
        public int TableIndex { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public int RowSpan { get; set; }
        public int ColumnSpan { get; set; }
        public string Direction { get; set; }
        public string Width { get; set; }
        public string Color { get; set; }
        public int? BaseBorderFillId { get; set; }
        public string ReportPath { get; set; }
    }

    internal sealed class TableCellDiagonalResult
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
        public string Direction { get; set; }
        public string Width { get; set; }
        public string Color { get; set; }
        public int? BaseBorderFillId { get; set; }
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
