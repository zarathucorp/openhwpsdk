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
    internal static class HwpxTableRowEditor
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableRowOperationResult Apply(TableRowOperationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.InputPath))
            {
                throw new ArgumentException("Input path is required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                throw new ArgumentException("Output path is required.", nameof(options));
            }

            var action = (options.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "add" && action != "delete")
            {
                throw new ArgumentException("action must be add or delete.", nameof(options));
            }

            if (options.TableIndex < 0)
            {
                throw new ArgumentException("table index must be zero or greater.", nameof(options));
            }

            if (options.Count <= 0)
            {
                options.Count = 1;
            }

            var result = new TableRowOperationResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                Action = action,
                RequestedRow = options.RowIndex,
                Count = options.Count,
                Note = "table row operation was not applied"
            };

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

            var nextObjectId = HwpxSectionPartResolver.NextBodyObjectId(entries);
            if (action == "add")
            {
                if (!ApplyAddRows(table, result, textRows, textStyleGuard, ref nextObjectId, out var note))
                {
                    result.Note = note;
                    WriteReport(result, options.ReportPath);
                    return result;
                }
            }
            else if (!ApplyDeleteRows(table, result, out var note))
            {
                result.Note = note;
                WriteReport(result, options.ReportPath);
                return result;
            }

            NormalizeTableRows(table);
            UpdateTableSize(table);
            result.NewRows = HwpxTableModel.BuildGrid(table).RowCount;
            result.NewColumns = HwpxTableModel.BuildGrid(table).ColumnCount;
            if (!ValidatePostconditions(result, out var postconditionReason))
            {
                result.Note = postconditionReason;
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.Applied = true;
            result.Note = "table row operation applied";

            entries[sectionPart] = SerializeXml(section);
            UpdatePreviewText(entries);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static bool ApplyAddRows(
            XElement table,
            TableRowOperationResult result,
            IList<IList<string>> textRows,
            HwpxTextStyleGuard textStyleGuard,
            ref long nextObjectId,
            out string note)
        {
            var rows = table.Elements(Hp + "tr").ToList();
            if (rows.Count == 0)
            {
                note = "table has no rows to clone";
                return false;
            }

            var afterRow = result.RequestedRow.HasValue ? result.RequestedRow.Value : rows.Count - 1;
            if (afterRow < 0 || afterRow >= rows.Count)
            {
                note = "row index is out of range for add";
                return false;
            }

            var templateRow = rows[afterRow];
            var insertAfter = templateRow;
            for (var index = 0; index < result.Count; index++)
            {
                var newRow = new XElement(templateRow);
                ReassignNumericIds(newRow, ref nextObjectId);
                SetRowText(newRow, index < textRows.Count ? textRows[index] : new List<string>(), textStyleGuard, ref nextObjectId, result);
                insertAfter.AddAfterSelf(newRow);
                insertAfter = newRow;
                result.AddedRows++;
            }

            result.EffectiveRow = afterRow;
            note = string.Empty;
            return true;
        }

        private static bool ValidatePostconditions(TableRowOperationResult result, out string reason)
        {
            var expectedRows = result.Action == "add"
                ? result.OriginalRows + result.Count
                : result.OriginalRows - result.Count;
            if (result.NewRows != expectedRows)
            {
                reason = "postcondition failed: row count is " + result.NewRows.ToString(CultureInfo.InvariantCulture) +
                         " but expected " + expectedRows.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (result.NewColumns != result.OriginalColumns)
            {
                reason = "postcondition failed: column count changed " +
                         result.OriginalColumns.ToString(CultureInfo.InvariantCulture) + "->" +
                         result.NewColumns.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool ApplyDeleteRows(XElement table, TableRowOperationResult result, out string note)
        {
            var rows = table.Elements(Hp + "tr").ToList();
            if (rows.Count == 0)
            {
                note = "table has no rows";
                return false;
            }

            if (!result.RequestedRow.HasValue)
            {
                note = "--row is required for delete";
                return false;
            }

            var startRow = result.RequestedRow.Value;
            if (startRow < 0 || startRow >= rows.Count)
            {
                note = "row index is out of range for delete";
                return false;
            }

            if (startRow + result.Count > rows.Count)
            {
                note = "delete count exceeds table rows";
                return false;
            }

            if (rows.Count - result.Count <= 0)
            {
                note = "delete would remove every table row";
                return false;
            }

            for (var index = 0; index < result.Count; index++)
            {
                rows[startRow + index].Remove();
                result.DeletedRows++;
            }

            result.EffectiveRow = startRow;
            note = string.Empty;
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
                reason = "table row package operation does not support merged cells yet";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(cell => cell.Descendants(Hp + "tbl").Any()))
            {
                reason = "table row package operation does not support nested tables yet";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(ContainsUnsupportedCellObject))
            {
                reason = "table row package operation only supports text-only cells";
                return false;
            }

            var declaredRows = GetInt(table, "rowCnt", grid.RowCount);
            var declaredColumns = GetInt(table, "colCnt", grid.ColumnCount);
            if (declaredRows != grid.RowCount || declaredColumns != grid.ColumnCount)
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
            if (cell == null)
            {
                return false;
            }

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

        private static void SetRowText(
            XElement row,
            IList<string> values,
            HwpxTextStyleGuard textStyleGuard,
            ref long nextObjectId,
            TableRowOperationResult result)
        {
            var cells = row.Elements(Hp + "tc").ToList();
            for (var columnIndex = 0; columnIndex < cells.Count; columnIndex++)
            {
                var text = values != null && columnIndex < values.Count ? values[columnIndex] : string.Empty;
                var paragraphs = HwpxTableModel.GetDirectCellParagraphs(cells[columnIndex])
                    .Where(item => !item.Descendants(Hp + "tbl").Any())
                    .ToList();
                if (paragraphs.Count == 0)
                {
                    continue;
                }

                paragraphs[0].SetAttributeValue("id", NextObjectId(ref nextObjectId));
                SetParagraphText(paragraphs[0], text);
                paragraphs.Skip(1).Remove();
                result.StyleRepairedRuns += textStyleGuard == null ? 0 : textStyleGuard.EnsureMinimumCharHeight(cells[columnIndex]);
                HwpxTextLayoutHelper.ExpandRowHeightForText(cells[columnIndex], text);
            }
        }

        private static void NormalizeTableRows(XElement table)
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
            var columns = rows.Select(row => row.Elements(Hp + "tc").Count()).DefaultIfEmpty(0).Max();
            table.SetAttributeValue("colCnt", columns.ToString(CultureInfo.InvariantCulture));
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

        private static void UpdateTextMatrixStats(TableRowOperationResult result, IList<IList<string>> rows)
        {
            if (result == null || result.Action != "add")
            {
                return;
            }

            var sourceRows = rows ?? new List<IList<string>>();
            result.TextRows = sourceRows.Count;
            result.TextMaxColumns = sourceRows.Select(row => row == null ? 0 : row.Count).DefaultIfEmpty(0).Max();

            var ignoredCells = 0;
            var missingCells = 0;
            for (var rowIndex = 0; rowIndex < result.Count; rowIndex++)
            {
                var row = rowIndex < sourceRows.Count ? sourceRows[rowIndex] : null;
                var sourceColumns = row == null ? 0 : row.Count;
                if (sourceColumns > result.OriginalColumns)
                {
                    ignoredCells += sourceColumns - result.OriginalColumns;
                }

                if (sourceColumns < result.OriginalColumns)
                {
                    missingCells += result.OriginalColumns - sourceColumns;
                }
            }

            for (var rowIndex = result.Count; rowIndex < sourceRows.Count; rowIndex++)
            {
                result.IgnoredTextRows++;
                ignoredCells += sourceRows[rowIndex] == null ? 0 : sourceRows[rowIndex].Count;
            }

            result.IgnoredTextCells = ignoredCells;
            result.MissingTextCells = missingCells;
        }

        private static int CountTopLevelTables(XDocument document)
        {
            return document == null || document.Root == null
                ? 0
                : document.Root.Descendants(Hp + "tbl").Count(table => !table.Ancestors(Hp + "tbl").Any());
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

        private static void WriteReport(TableRowOperationResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Row Package Operation");
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
            AppendReportRow(report, "requested row", result.RequestedRow.HasValue ? result.RequestedRow.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendReportRow(report, "effective row", result.EffectiveRow.HasValue ? result.EffectiveRow.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendReportRow(report, "count", result.Count.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original rows", result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new rows", result.NewRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original columns", result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new columns", result.NewColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "added rows", result.AddedRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "deleted rows", result.DeletedRows.ToString(CultureInfo.InvariantCulture));
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
            report.Append(EscapeMarkdown(field));
            report.Append(" | ");
            report.Append(EscapeMarkdown(value));
            report.AppendLine(" |");
        }

        private static string EscapeMarkdown(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }
    }

    internal sealed class TableRowOperationOptions
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public string Action { get; set; }

        public int TableIndex { get; set; }

        public int? RowIndex { get; set; }

        public int Count { get; set; }

        public string Text { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class TableRowOperationResult
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public bool Applied { get; set; }

        public string Action { get; set; }

        public int TableIndex { get; set; }

        public int TopLevelTableCount { get; set; }

        public int? RequestedRow { get; set; }

        public int? EffectiveRow { get; set; }

        public int Count { get; set; }

        public int OriginalRows { get; set; }

        public int NewRows { get; set; }

        public int OriginalColumns { get; set; }

        public int NewColumns { get; set; }

        public int AddedRows { get; set; }

        public int DeletedRows { get; set; }

        public int TextRows { get; set; }

        public int TextMaxColumns { get; set; }

        public int MissingTextCells { get; set; }

        public int IgnoredTextRows { get; set; }

        public int IgnoredTextCells { get; set; }

        public int StyleRepairedRuns { get; set; }

        public string Note { get; set; }
    }
}
