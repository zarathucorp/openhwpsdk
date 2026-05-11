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
    internal static class HwpxTableMergeEditor
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableMergeResult Merge(TableMergeOptions options)
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

            if (options.RowSpan <= 0 || options.ColumnSpan <= 0)
            {
                throw new ArgumentException("row-span and col-span must be greater than zero.", nameof(options));
            }

            var result = new TableMergeResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                Row = options.Row,
                Column = options.Column,
                RowSpan = options.RowSpan,
                ColumnSpan = options.ColumnSpan,
                Note = "table merge was not applied"
            };

            if (!string.Equals(result.Section, "section0", StringComparison.OrdinalIgnoreCase))
            {
                result.Note = "table merge package operation currently supports section0 only";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var entries = SimpleZipArchive.ReadAll(result.InputPath);
            var textStyleGuard = HwpxTextStyleGuard.Create(entries);
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
            if (!IsSimpleEditableTable(table, grid, out var rejectionReason))
            {
                result.Note = rejectionReason;
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (options.Row + options.RowSpan > grid.RowCount ||
                options.Column + options.ColumnSpan > grid.ColumnCount)
            {
                result.Note = "merge rectangle exceeds table bounds";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (options.RowSpan == 1 && options.ColumnSpan == 1)
            {
                result.Note = "merge rectangle must cover more than one cell";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!ApplyMerge(table, result, options.Text, textStyleGuard, out var note))
            {
                result.Note = note;
                WriteReport(result, options.ReportPath);
                return result;
            }

            var newGrid = HwpxTableModel.BuildGrid(table);
            if (!ValidatePostconditions(table, result, newGrid, out var postconditionReason))
            {
                result.Note = postconditionReason;
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.NewRows = newGrid.RowCount;
            result.NewColumns = newGrid.ColumnCount;
            result.Applied = true;
            result.Note = "table merge applied";

            entries[sectionPart] = SerializeXml(section);
            UpdatePreviewText(entries);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static bool ApplyMerge(XElement table, TableMergeResult result, string replacementText, HwpxTextStyleGuard textStyleGuard, out string note)
        {
            var topLeft = HwpxTableModel.FindDirectCellByAddress(table, result.Row, result.Column);
            if (topLeft == null)
            {
                note = "top-left merge cell was not found";
                return false;
            }

            var regionCells = new List<XElement>();
            for (var row = result.Row; row < result.Row + result.RowSpan; row++)
            {
                for (var column = result.Column; column < result.Column + result.ColumnSpan; column++)
                {
                    var cell = HwpxTableModel.FindDirectCellByAddress(table, row, column);
                    if (cell == null)
                    {
                        note = "merge rectangle includes a missing cell";
                        return false;
                    }

                    regionCells.Add(cell);
                }
            }

            var mergedText = replacementText == null
                ? string.Join("\n", regionCells.Select(cell => HwpxTableModel.DirectTextOfCell(cell).Trim()).Where(text => text.Length > 0).ToArray())
                : replacementText;

            EnsureCellChild(topLeft, "cellSpan").SetAttributeValue("rowSpan", result.RowSpan.ToString(CultureInfo.InvariantCulture));
            topLeft.Element(Hp + "cellSpan").SetAttributeValue("colSpan", result.ColumnSpan.ToString(CultureInfo.InvariantCulture));
            var size = EnsureCellChild(topLeft, "cellSz");
            size.SetAttributeValue("width", RegionWidth(table, result).ToString(CultureInfo.InvariantCulture));
            size.SetAttributeValue("height", RegionHeight(table, result).ToString(CultureInfo.InvariantCulture));
            SetCellText(topLeft, mergedText, textStyleGuard, result);

            foreach (var cell in regionCells)
            {
                if (object.ReferenceEquals(cell, topLeft))
                {
                    continue;
                }

                cell.Remove();
                result.RemovedCells++;
            }

            result.MergedTextLength = mergedText.Length;
            note = string.Empty;
            return true;
        }

        private static bool ValidatePostconditions(XElement table, TableMergeResult result, TableGrid newGrid, out string reason)
        {
            if (newGrid.RowCount != result.OriginalRows || newGrid.ColumnCount != result.OriginalColumns)
            {
                reason = "postcondition failed: merge changed table grid size";
                return false;
            }

            if (newGrid.OverlapIssues.Count > 0)
            {
                reason = "postcondition failed: merge created overlapping cell spans";
                return false;
            }

            var expectedRemovedCells = (result.RowSpan * result.ColumnSpan) - 1;
            if (result.RemovedCells != expectedRemovedCells)
            {
                reason = "postcondition failed: removed cell count is " +
                         result.RemovedCells.ToString(CultureInfo.InvariantCulture) +
                         " but expected " + expectedRemovedCells.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            var topLeft = HwpxTableModel.FindDirectCellByAddress(table, result.Row, result.Column);
            var span = topLeft == null ? null : topLeft.Element(Hp + "cellSpan");
            if (topLeft == null || span == null ||
                GetInt(span, "rowSpan", 1) != result.RowSpan ||
                GetInt(span, "colSpan", 1) != result.ColumnSpan)
            {
                reason = "postcondition failed: top-left cell span does not match requested merge rectangle";
                return false;
            }

            for (var row = result.Row; row < result.Row + result.RowSpan; row++)
            {
                for (var column = result.Column; column < result.Column + result.ColumnSpan; column++)
                {
                    if (row == result.Row && column == result.Column)
                    {
                        continue;
                    }

                    if (HwpxTableModel.FindDirectCellByAddress(table, row, column) != null)
                    {
                        reason = "postcondition failed: covered cell r" +
                                 row.ToString(CultureInfo.InvariantCulture) + "c" +
                                 column.ToString(CultureInfo.InvariantCulture) + " still exists";
                        return false;
                    }
                }
            }

            var expectedPhysicalCells = (result.OriginalRows * result.OriginalColumns) - expectedRemovedCells;
            if (newGrid.Cells.Count != expectedPhysicalCells)
            {
                reason = "postcondition failed: physical cell count is " +
                         newGrid.Cells.Count.ToString(CultureInfo.InvariantCulture) +
                         " but expected " + expectedPhysicalCells.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static int RegionWidth(XElement table, TableMergeResult result)
        {
            var width = 0;
            for (var column = result.Column; column < result.Column + result.ColumnSpan; column++)
            {
                var cell = HwpxTableModel.FindDirectCellByAddress(table, result.Row, column);
                var size = cell == null ? null : cell.Element(Hp + "cellSz");
                width += size == null ? 0 : GetInt(size, "width", 0);
            }

            return width;
        }

        private static int RegionHeight(XElement table, TableMergeResult result)
        {
            var height = 0;
            for (var row = result.Row; row < result.Row + result.RowSpan; row++)
            {
                var cell = HwpxTableModel.FindDirectCellByAddress(table, row, result.Column);
                var size = cell == null ? null : cell.Element(Hp + "cellSz");
                height += size == null ? 0 : GetInt(size, "height", 0);
            }

            return height;
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
                reason = "table merge package operation currently starts from unmerged tables only";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(cell => cell.Descendants(Hp + "tbl").Any()))
            {
                reason = "table merge package operation does not support nested tables yet";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(ContainsUnsupportedCellObject))
            {
                reason = "table merge package operation only supports text-only cells";
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
                    var address = cells[columnIndex].Element(Hp + "cellAddr");
                    var span = cells[columnIndex].Element(Hp + "cellSpan");
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
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ContainsUnsupportedCellObject(XElement cell)
        {
            foreach (var element in cell.Descendants())
            {
                if (element.Name.Namespace != Hp)
                {
                    continue;
                }

                var name = element.Name.LocalName;
                if (name == "subList" ||
                    name == "p" ||
                    name == "run" ||
                    name == "t" ||
                    name == "linesegarray" ||
                    name == "lineseg" ||
                    name == "cellAddr" ||
                    name == "cellSpan" ||
                    name == "cellSz" ||
                    name == "cellMargin")
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static void SetCellText(XElement cell, string text, HwpxTextStyleGuard textStyleGuard, TableMergeResult result)
        {
            var paragraphs = HwpxTableModel.GetDirectCellParagraphs(cell)
                .Where(item => !item.Descendants(Hp + "tbl").Any())
                .ToList();
            if (paragraphs.Count == 0)
            {
                return;
            }

            SetParagraphText(paragraphs[0], text);
            paragraphs.Skip(1).Remove();
            result.StyleRepairedRuns += textStyleGuard == null ? 0 : textStyleGuard.EnsureMinimumCharHeight(cell);
            HwpxTextLayoutHelper.ExpandRowHeightForText(cell, text);
        }

        private static void SetParagraphText(XElement paragraph, string text)
        {
            var textNodes = HwpxTableModel.DirectTextNodesOfParagraph(paragraph).ToList();
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

            firstTextNode.Value = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            for (var index = 1; index < textNodes.Count; index++)
            {
                textNodes[index].Value = string.Empty;
            }

            paragraph.Elements(Hp + "linesegarray").Remove();
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

        private static string NormalizeSection(string section)
        {
            var value = string.IsNullOrWhiteSpace(section) ? "section0" : section.Trim();
            if (value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                value = Path.GetFileNameWithoutExtension(value);
            }

            return value;
        }

        private static void UpdatePreviewText(IDictionary<string, byte[]> entries)
        {
            if (entries == null || !entries.ContainsKey("Preview/PrvText.txt"))
            {
                return;
            }

            var builder = new StringBuilder();
            foreach (var entry in entries.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!entry.Key.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) ||
                    !entry.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                XDocument document;
                try
                {
                    document = XDocument.Parse(Encoding.UTF8.GetString(entry.Value), LoadOptions.PreserveWhitespace);
                }
                catch (XmlException)
                {
                    continue;
                }

                foreach (var paragraph in document.Descendants(Hp + "p"))
                {
                    var text = (HwpxTableModel.TextOfDirectParagraph(paragraph) ?? string.Empty).Trim();
                    if (text.Length > 0)
                    {
                        builder.AppendLine(text);
                    }
                }
            }

            entries["Preview/PrvText.txt"] = new UTF8Encoding(false).GetBytes(builder.ToString());
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

        private static void WriteReport(TableMergeResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Merge Package Operation");
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
            AppendReportRow(report, "original rows", result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new rows", result.NewRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original columns", result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new columns", result.NewColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "removed cells", result.RemovedCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "merged text length", result.MergedTextLength.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "style repaired runs", result.StyleRepairedRuns.ToString(CultureInfo.InvariantCulture));
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

    internal sealed class TableMergeOptions
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public int TableIndex { get; set; }

        public int Row { get; set; }

        public int Column { get; set; }

        public int RowSpan { get; set; }

        public int ColumnSpan { get; set; }

        public string Text { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class TableMergeResult
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

        public int OriginalRows { get; set; }

        public int NewRows { get; set; }

        public int OriginalColumns { get; set; }

        public int NewColumns { get; set; }

        public int RemovedCells { get; set; }

        public int MergedTextLength { get; set; }

        public int StyleRepairedRuns { get; set; }

        public string Note { get; set; }
    }
}
