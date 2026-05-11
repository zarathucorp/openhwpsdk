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
    internal static class HwpxTableColumnEditor
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableColumnOperationResult Apply(TableColumnOperationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var action = (options.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "add" && action != "delete")
            {
                throw new ArgumentException("action must be add or delete.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.InputPath) || string.IsNullOrWhiteSpace(options.OutputPath))
            {
                throw new ArgumentException("input and output paths are required.", nameof(options));
            }

            if (options.TableIndex < 0)
            {
                throw new ArgumentException("table index must be zero or greater.", nameof(options));
            }

            if (options.Count <= 0)
            {
                options.Count = 1;
            }

            var result = new TableColumnOperationResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                Action = action,
                RequestedColumn = options.ColumnIndex,
                Count = options.Count,
                Note = "table column operation was not applied"
            };

            if (!string.Equals(result.Section, "section0", StringComparison.OrdinalIgnoreCase))
            {
                result.Note = "table column package operation currently supports section0 only";
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

            var textRows = ParseTableText(options.Text);
            UpdateTextMatrixStats(result, textRows);

            var nextObjectId = Math.Max(1, MaxNumericId(section) + 1);
            if (action == "add")
            {
                if (!ApplyAddColumns(table, result, textRows, textStyleGuard, ref nextObjectId, out var note))
                {
                    result.Note = note;
                    WriteReport(result, options.ReportPath);
                    return result;
                }
            }
            else if (!ApplyDeleteColumns(table, result, out var note))
            {
                result.Note = note;
                WriteReport(result, options.ReportPath);
                return result;
            }

            NormalizeTable(table);
            UpdateTableSize(table);
            var newGrid = HwpxTableModel.BuildGrid(table);
            result.NewRows = newGrid.RowCount;
            result.NewColumns = newGrid.ColumnCount;
            if (!ValidatePostconditions(result, out var postconditionReason))
            {
                result.Note = postconditionReason;
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.Applied = true;
            result.Note = "table column operation applied";
            entries[sectionPart] = SerializeXml(section);
            UpdatePreviewText(entries);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static bool ApplyAddColumns(
            XElement table,
            TableColumnOperationResult result,
            IList<IList<string>> textRows,
            HwpxTextStyleGuard textStyleGuard,
            ref long nextObjectId,
            out string note)
        {
            var rows = table.Elements(Hp + "tr").ToList();
            var afterColumn = result.RequestedColumn.HasValue ? result.RequestedColumn.Value : result.OriginalColumns - 1;
            if (afterColumn < 0 || afterColumn >= result.OriginalColumns)
            {
                note = "column index is out of range for add";
                return false;
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var cells = rows[rowIndex].Elements(Hp + "tc").ToList();
                var insertAfter = cells[afterColumn];
                for (var addIndex = 0; addIndex < result.Count; addIndex++)
                {
                    var newCell = new XElement(cells[afterColumn]);
                    ReassignNumericIds(newCell, ref nextObjectId);
                    SetCellText(newCell, TextAt(textRows, rowIndex, addIndex), textStyleGuard, ref nextObjectId, result);
                    insertAfter.AddAfterSelf(newCell);
                    insertAfter = newCell;
                    result.AddedCells++;
                }
            }

            result.EffectiveColumn = afterColumn;
            result.AddedColumns = result.Count;
            note = string.Empty;
            return true;
        }

        private static bool ApplyDeleteColumns(XElement table, TableColumnOperationResult result, out string note)
        {
            if (!result.RequestedColumn.HasValue)
            {
                note = "--column is required for delete";
                return false;
            }

            var startColumn = result.RequestedColumn.Value;
            if (startColumn < 0 || startColumn >= result.OriginalColumns)
            {
                note = "column index is out of range for delete";
                return false;
            }

            if (startColumn + result.Count > result.OriginalColumns)
            {
                note = "delete count exceeds table columns";
                return false;
            }

            if (result.OriginalColumns - result.Count <= 0)
            {
                note = "delete would remove every table column";
                return false;
            }

            foreach (var row in table.Elements(Hp + "tr").ToList())
            {
                var cells = row.Elements(Hp + "tc").ToList();
                for (var index = 0; index < result.Count; index++)
                {
                    cells[startColumn + index].Remove();
                    result.DeletedCells++;
                }
            }

            result.EffectiveColumn = startColumn;
            result.DeletedColumns = result.Count;
            note = string.Empty;
            return true;
        }

        private static bool ValidatePostconditions(TableColumnOperationResult result, out string reason)
        {
            var expectedColumns = result.Action == "add"
                ? result.OriginalColumns + result.Count
                : result.OriginalColumns - result.Count;
            if (result.NewColumns != expectedColumns)
            {
                reason = "postcondition failed: column count is " + result.NewColumns.ToString(CultureInfo.InvariantCulture) +
                         " but expected " + expectedColumns.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (result.NewRows != result.OriginalRows)
            {
                reason = "postcondition failed: row count changed " +
                         result.OriginalRows.ToString(CultureInfo.InvariantCulture) + "->" +
                         result.NewRows.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            reason = string.Empty;
            return true;
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
                reason = "table column package operation does not support merged cells yet";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(cell => cell.Descendants(Hp + "tbl").Any()))
            {
                reason = "table column package operation does not support nested tables yet";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(ContainsUnsupportedCellObject))
            {
                reason = "table column package operation only supports text-only cells";
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

        private static void SetCellText(
            XElement cell,
            string text,
            HwpxTextStyleGuard textStyleGuard,
            ref long nextObjectId,
            TableColumnOperationResult result)
        {
            var paragraphs = HwpxTableModel.GetDirectCellParagraphs(cell)
                .Where(item => !item.Descendants(Hp + "tbl").Any())
                .ToList();
            if (paragraphs.Count == 0)
            {
                return;
            }

            paragraphs[0].SetAttributeValue("id", NextObjectId(ref nextObjectId));
            SetParagraphText(paragraphs[0], text);
            paragraphs.Skip(1).Remove();
            result.StyleRepairedRuns += textStyleGuard == null ? 0 : textStyleGuard.EnsureMinimumCharHeight(cell);
            HwpxTextLayoutHelper.ExpandRowHeightForText(cell, text);
        }

        private static void NormalizeTable(XElement table)
        {
            var rows = table.Elements(Hp + "tr").ToList();
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var cells = rows[rowIndex].Elements(Hp + "tc").ToList();
                for (var columnIndex = 0; columnIndex < cells.Count; columnIndex++)
                {
                    var cell = cells[columnIndex];
                    EnsureCellChild(cell, "cellAddr").SetAttributeValue("rowAddr", rowIndex.ToString(CultureInfo.InvariantCulture));
                    cell.Element(Hp + "cellAddr").SetAttributeValue("colAddr", columnIndex.ToString(CultureInfo.InvariantCulture));
                    EnsureCellChild(cell, "cellSpan").SetAttributeValue("rowSpan", "1");
                    cell.Element(Hp + "cellSpan").SetAttributeValue("colSpan", "1");
                }
            }

            table.SetAttributeValue("rowCnt", rows.Count.ToString(CultureInfo.InvariantCulture));
            table.SetAttributeValue("colCnt", rows.Select(row => row.Elements(Hp + "tc").Count()).DefaultIfEmpty(0).Max().ToString(CultureInfo.InvariantCulture));
        }

        private static void UpdateTableSize(XElement table)
        {
            var rows = table == null ? new List<XElement>() : table.Elements(Hp + "tr").ToList();
            var firstRow = rows.FirstOrDefault();
            var width = firstRow == null
                ? 0
                : firstRow.Elements(Hp + "tc")
                    .Select(cell =>
                    {
                        var size = cell.Element(Hp + "cellSz");
                        return size == null ? 0 : GetInt(size, "width", 0);
                    })
                    .Sum();
            var height = rows.Select(row => row.Elements(Hp + "tc")
                    .Select(cell =>
                    {
                        var size = cell.Element(Hp + "cellSz");
                        return size == null ? 0 : GetInt(size, "height", 0);
                    })
                    .DefaultIfEmpty(0)
                    .Max())
                .Sum();

            var sizeElement = table == null ? null : table.Element(Hp + "sz");
            if (sizeElement == null)
            {
                return;
            }

            if (width > 0)
            {
                sizeElement.SetAttributeValue("width", width.ToString(CultureInfo.InvariantCulture));
            }

            if (height > 0)
            {
                sizeElement.SetAttributeValue("height", height.ToString(CultureInfo.InvariantCulture));
            }
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

            firstTextNode.Value = NormalizePackageWriteText(text);
            for (var index = 1; index < textNodes.Count; index++)
            {
                textNodes[index].Value = string.Empty;
            }

            paragraph.Elements(Hp + "linesegarray").Remove();
        }

        private static void ReassignNumericIds(XElement scope, ref long nextObjectId)
        {
            foreach (var attribute in scope.DescendantsAndSelf().Attributes("id").ToList())
            {
                long value;
                if (long.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    attribute.Value = NextObjectId(ref nextObjectId);
                }
            }
        }

        private static IList<IList<string>> ParseTableText(string raw)
        {
            var rows = new List<IList<string>>();
            if (string.IsNullOrEmpty(raw))
            {
                return rows;
            }

            foreach (var row in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { ';', '\n' }, StringSplitOptions.None))
            {
                rows.Add(row.Split('|').ToList());
            }

            return rows;
        }

        private static void UpdateTextMatrixStats(TableColumnOperationResult result, IList<IList<string>> rows)
        {
            var sourceRows = rows ?? new List<IList<string>>();
            result.TextRows = sourceRows.Count;
            result.TextMaxColumns = sourceRows.Select(row => row == null ? 0 : row.Count).DefaultIfEmpty(0).Max();
            if (result.Action != "add")
            {
                return;
            }

            for (var rowIndex = 0; rowIndex < result.OriginalRows; rowIndex++)
            {
                var row = rowIndex < sourceRows.Count ? sourceRows[rowIndex] : null;
                var sourceColumns = row == null ? 0 : row.Count;
                if (sourceColumns > result.Count)
                {
                    result.IgnoredTextCells += sourceColumns - result.Count;
                }

                if (sourceColumns < result.Count)
                {
                    result.MissingTextCells += result.Count - sourceColumns;
                }
            }

            for (var rowIndex = result.OriginalRows; rowIndex < sourceRows.Count; rowIndex++)
            {
                result.IgnoredTextRows++;
                result.IgnoredTextCells += sourceRows[rowIndex] == null ? 0 : sourceRows[rowIndex].Count;
            }
        }

        private static string TextAt(IList<IList<string>> rows, int rowIndex, int columnIndex)
        {
            if (rows == null || rowIndex < 0 || rowIndex >= rows.Count)
            {
                return string.Empty;
            }

            var row = rows[rowIndex];
            if (row == null || columnIndex < 0 || columnIndex >= row.Count)
            {
                return string.Empty;
            }

            return row[columnIndex] ?? string.Empty;
        }

        private static long MaxNumericId(XDocument document)
        {
            long max = 0;
            if (document == null)
            {
                return max;
            }

            foreach (var attribute in document.Descendants().Attributes("id"))
            {
                long value;
                if (long.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private static string NextObjectId(ref long nextObjectId)
        {
            var value = nextObjectId;
            nextObjectId++;
            return value.ToString(CultureInfo.InvariantCulture);
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

        private static string NormalizePackageWriteText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
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

        private static void WriteReport(TableColumnOperationResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Column Package Operation");
            report.AppendLine();
            report.AppendLine("- input: `" + result.InputPath + "`");
            report.AppendLine("- output: `" + result.OutputPath + "`");
            report.AppendLine("- section: `" + result.Section + "`");
            report.AppendLine("- verdict: " + (result.Applied ? "applied" : "failed"));
            report.AppendLine();
            report.AppendLine("| field | value |");
            report.AppendLine("| --- | --- |");
            AppendReportRow(report, "action", result.Action);
            AppendReportRow(report, "table index", result.TableIndex.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "top-level table count", result.TopLevelTableCount.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "requested column", result.RequestedColumn.HasValue ? result.RequestedColumn.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendReportRow(report, "effective column", result.EffectiveColumn.HasValue ? result.EffectiveColumn.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendReportRow(report, "count", result.Count.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original rows", result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new rows", result.NewRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original columns", result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new columns", result.NewColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "added columns", result.AddedColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "deleted columns", result.DeletedColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "added cells", result.AddedCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "deleted cells", result.DeletedCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "text rows", result.TextRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "text max columns", result.TextMaxColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "missing text cells", result.MissingTextCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "ignored text rows", result.IgnoredTextRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "ignored text cells", result.IgnoredTextCells.ToString(CultureInfo.InvariantCulture));
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

    internal sealed class TableColumnOperationOptions
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public string Action { get; set; }

        public int TableIndex { get; set; }

        public int? ColumnIndex { get; set; }

        public int Count { get; set; }

        public string Text { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class TableColumnOperationResult
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public bool Applied { get; set; }

        public string Action { get; set; }

        public int TableIndex { get; set; }

        public int TopLevelTableCount { get; set; }

        public int? RequestedColumn { get; set; }

        public int? EffectiveColumn { get; set; }

        public int Count { get; set; }

        public int OriginalRows { get; set; }

        public int NewRows { get; set; }

        public int OriginalColumns { get; set; }

        public int NewColumns { get; set; }

        public int AddedColumns { get; set; }

        public int DeletedColumns { get; set; }

        public int AddedCells { get; set; }

        public int DeletedCells { get; set; }

        public int TextRows { get; set; }

        public int TextMaxColumns { get; set; }

        public int MissingTextCells { get; set; }

        public int IgnoredTextRows { get; set; }

        public int IgnoredTextCells { get; set; }

        public int StyleRepairedRuns { get; set; }

        public string Note { get; set; }
    }
}
