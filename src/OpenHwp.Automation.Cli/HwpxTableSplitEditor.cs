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
    internal static class HwpxTableSplitEditor
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableSplitResult Split(TableSplitOptions options)
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

            var result = new TableSplitResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                TableIndex = options.TableIndex,
                Row = options.Row,
                Column = options.Column,
                Note = "table split was not applied"
            };

            if (!string.Equals(result.Section, "section0", StringComparison.OrdinalIgnoreCase))
            {
                result.Note = "table split package operation currently supports section0 only";
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
            result.OriginalPhysicalCells = grid.Cells.Count;
            if (!IsEditableTable(table, grid, out var rejectionReason))
            {
                result.Note = rejectionReason;
                WriteReport(result, options.ReportPath);
                return result;
            }

            var target = grid.Cells.FirstOrDefault(cell => cell.Row == options.Row && cell.Column == options.Column);
            if (target == null)
            {
                result.Note = "target cell was not found";
                WriteReport(result, options.ReportPath);
                return result;
            }

            if (!object.ReferenceEquals(target.Element, HwpxTableModel.FindDirectCellByAddress(table, options.Row, options.Column)))
            {
                result.Note = "target coordinate is covered by another merged cell; use the top-left cell address";
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.RowSpan = target.RowSpan;
            result.ColumnSpan = target.ColumnSpan;
            if (!target.Merged)
            {
                result.Note = "target cell is not merged";
                WriteReport(result, options.ReportPath);
                return result;
            }

            var textRows = ParseTableText(options.Text);
            UpdateTextMatrixStats(result, textRows, options.Text != null);

            var nextObjectId = Math.Max(1, MaxNumericId(section.Root) + 1);
            if (!ApplySplit(table, grid, target, result, textRows, textStyleGuard, ref nextObjectId, out var note))
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
            result.NewPhysicalCells = newGrid.Cells.Count;
            result.Applied = true;
            result.Note = "table split applied";

            entries[sectionPart] = SerializeXml(section);
            UpdatePreviewText(entries);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static bool ApplySplit(
            XElement table,
            TableGrid originalGrid,
            TableGridCell target,
            TableSplitResult result,
            IList<IList<string>> textRows,
            HwpxTextStyleGuard textStyleGuard,
            ref long nextObjectId,
            out string note)
        {
            var targetCell = target.Element;
            var rows = table.Elements(Hp + "tr").ToList();
            if (target.Row + target.RowSpan > rows.Count)
            {
                note = "target span exceeds physical table rows";
                return false;
            }

            var columnWidths = ResolveColumnWidths(originalGrid, target);
            var rowHeights = ResolveRowHeights(originalGrid, target);
            var originalText = HwpxTableModel.DirectTextOfCell(targetCell);
            var prototype = new XElement(targetCell);

            SetCellAddress(targetCell, target.Row, target.Column);
            SetCellSpan(targetCell, 1, 1);
            SetCellSize(targetCell, columnWidths[0], rowHeights[0]);
            SetCellText(targetCell, TextAt(textRows, 0, 0, originalText), textStyleGuard, ref nextObjectId, result);

            for (var rowOffset = 0; rowOffset < target.RowSpan; rowOffset++)
            {
                var rowElement = rows[target.Row + rowOffset];
                for (var columnOffset = 0; columnOffset < target.ColumnSpan; columnOffset++)
                {
                    if (rowOffset == 0 && columnOffset == 0)
                    {
                        continue;
                    }

                    var newCell = new XElement(prototype);
                    ReassignNumericIds(newCell, ref nextObjectId);
                    var rowAddress = target.Row + rowOffset;
                    var columnAddress = target.Column + columnOffset;
                    SetCellAddress(newCell, rowAddress, columnAddress);
                    SetCellSpan(newCell, 1, 1);
                    SetCellSize(newCell, columnWidths[columnOffset], rowHeights[rowOffset]);
                    SetCellText(newCell, TextAt(textRows, rowOffset, columnOffset, string.Empty), textStyleGuard, ref nextObjectId, result);
                    InsertCellByColumnAddress(rowElement, newCell, columnAddress);
                    result.AddedCells++;
                }
            }

            note = string.Empty;
            return true;
        }

        private static bool ValidatePostconditions(XElement table, TableSplitResult result, TableGrid newGrid, out string reason)
        {
            if (newGrid.RowCount != result.OriginalRows || newGrid.ColumnCount != result.OriginalColumns)
            {
                reason = "postcondition failed: split changed table grid size";
                return false;
            }

            if (newGrid.OverlapIssues.Count > 0)
            {
                reason = "postcondition failed: split created overlapping cell spans";
                return false;
            }

            if (!IsFullyCovered(newGrid))
            {
                reason = "postcondition failed: split left uncovered grid coordinates";
                return false;
            }

            var expectedAddedCells = (result.RowSpan * result.ColumnSpan) - 1;
            if (result.AddedCells != expectedAddedCells)
            {
                reason = "postcondition failed: added cell count is " +
                         result.AddedCells.ToString(CultureInfo.InvariantCulture) +
                         " but expected " + expectedAddedCells.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            var target = HwpxTableModel.FindDirectCellByAddress(table, result.Row, result.Column);
            var span = target == null ? null : target.Element(Hp + "cellSpan");
            if (target == null || span == null ||
                GetInt(span, "rowSpan", 1) != 1 ||
                GetInt(span, "colSpan", 1) != 1)
            {
                reason = "postcondition failed: target cell was not reset to 1x1 span";
                return false;
            }

            for (var row = result.Row; row < result.Row + result.RowSpan; row++)
            {
                for (var column = result.Column; column < result.Column + result.ColumnSpan; column++)
                {
                    var cell = HwpxTableModel.FindDirectCellByAddress(table, row, column);
                    if (cell == null)
                    {
                        reason = "postcondition failed: split cell r" +
                                 row.ToString(CultureInfo.InvariantCulture) + "c" +
                                 column.ToString(CultureInfo.InvariantCulture) + " was not created";
                        return false;
                    }

                    var cellSpan = cell.Element(Hp + "cellSpan");
                    if (cellSpan == null ||
                        GetInt(cellSpan, "rowSpan", 1) != 1 ||
                        GetInt(cellSpan, "colSpan", 1) != 1)
                    {
                        reason = "postcondition failed: split cell r" +
                                 row.ToString(CultureInfo.InvariantCulture) + "c" +
                                 column.ToString(CultureInfo.InvariantCulture) + " is still merged";
                        return false;
                    }
                }
            }

            var expectedPhysicalCells = result.OriginalPhysicalCells + expectedAddedCells;
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

        private static int[] ResolveColumnWidths(TableGrid grid, TableGridCell target)
        {
            var widths = new int[target.ColumnSpan];
            for (var offset = 0; offset < target.ColumnSpan; offset++)
            {
                var column = target.Column + offset;
                var sample = grid.Cells
                    .Where(cell => !object.ReferenceEquals(cell.Element, target.Element) &&
                                   cell.Column == column &&
                                   cell.ColumnSpan == 1)
                    .Select(cell => GetCellWidth(cell.Element))
                    .FirstOrDefault(value => value > 0);
                widths[offset] = sample;
            }

            DistributeMissingSizes(widths, Math.Max(GetCellWidth(target.Element), 0));
            return widths;
        }

        private static int[] ResolveRowHeights(TableGrid grid, TableGridCell target)
        {
            var heights = new int[target.RowSpan];
            for (var offset = 0; offset < target.RowSpan; offset++)
            {
                var row = target.Row + offset;
                var sample = grid.Cells
                    .Where(cell => !object.ReferenceEquals(cell.Element, target.Element) &&
                                   cell.Row == row &&
                                   cell.RowSpan == 1)
                    .Select(cell => GetCellHeight(cell.Element))
                    .DefaultIfEmpty(0)
                    .Max();
                heights[offset] = sample;
            }

            DistributeMissingSizes(heights, Math.Max(GetCellHeight(target.Element), 0));
            return heights;
        }

        private static void DistributeMissingSizes(int[] sizes, int total)
        {
            if (sizes == null || sizes.Length == 0)
            {
                return;
            }

            var known = sizes.Where(value => value > 0).Sum();
            var missingIndexes = sizes
                .Select((value, index) => new { value, index })
                .Where(item => item.value <= 0)
                .Select(item => item.index)
                .ToList();
            if (missingIndexes.Count == 0)
            {
                return;
            }

            var remaining = total > known ? total - known : 0;
            var fallback = remaining > 0 ? Math.Max(1, remaining / missingIndexes.Count) : Math.Max(1, total / sizes.Length);
            for (var index = 0; index < missingIndexes.Count; index++)
            {
                sizes[missingIndexes[index]] = fallback;
            }
        }

        private static bool IsEditableTable(XElement table, TableGrid grid, out string reason)
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

            if (!IsFullyCovered(grid))
            {
                reason = "table grid has uncovered coordinates";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(cell => cell.Descendants(Hp + "tbl").Any()))
            {
                reason = "table split package operation does not support nested tables yet";
                return false;
            }

            if (HwpxTableModel.DirectCells(table).Any(ContainsUnsupportedCellObject))
            {
                reason = "table split package operation only supports text-only cells";
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

            if (!HasConsistentPhysicalRows(rows, grid.ColumnCount, out reason))
            {
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool HasConsistentPhysicalRows(IList<XElement> rows, int columnCount, out string reason)
        {
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var previousColumn = -1;
                foreach (var cell in rows[rowIndex].Elements(Hp + "tc"))
                {
                    var address = cell.Element(Hp + "cellAddr");
                    var span = cell.Element(Hp + "cellSpan");
                    if (address == null || span == null)
                    {
                        reason = "table cell is missing cellAddr or cellSpan";
                        return false;
                    }

                    var rowAddress = GetInt(address, "rowAddr", -1);
                    var columnAddress = GetInt(address, "colAddr", -1);
                    var rowSpan = GetInt(span, "rowSpan", 1);
                    var columnSpan = GetInt(span, "colSpan", 1);
                    if (rowAddress != rowIndex)
                    {
                        reason = "table physical row does not match cellAddr rowAddr";
                        return false;
                    }

                    if (columnAddress <= previousColumn)
                    {
                        reason = "table row cellAddr colAddr values are not in ascending order";
                        return false;
                    }

                    if (columnAddress < 0 ||
                        columnAddress >= columnCount ||
                        rowSpan <= 0 ||
                        columnSpan <= 0 ||
                        rowAddress + rowSpan > rows.Count ||
                        columnAddress + columnSpan > columnCount)
                    {
                        reason = "table cell span exceeds declared grid";
                        return false;
                    }

                    previousColumn = columnAddress;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool IsFullyCovered(TableGrid grid)
        {
            if (grid == null)
            {
                return false;
            }

            for (var row = 0; row < grid.RowCount; row++)
            {
                for (var column = 0; column < grid.ColumnCount; column++)
                {
                    if (!grid.Cells.Any(cell =>
                        row >= cell.Row &&
                        row < cell.Row + cell.RowSpan &&
                        column >= cell.Column &&
                        column < cell.Column + cell.ColumnSpan))
                    {
                        return false;
                    }
                }
            }

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

        private static void InsertCellByColumnAddress(XElement row, XElement newCell, int columnAddress)
        {
            var cells = row.Elements(Hp + "tc").ToList();
            var before = cells.FirstOrDefault(cell =>
            {
                var address = cell.Element(Hp + "cellAddr");
                return address != null && GetInt(address, "colAddr", int.MaxValue) > columnAddress;
            });
            if (before == null)
            {
                row.Add(newCell);
            }
            else
            {
                before.AddBeforeSelf(newCell);
            }
        }

        private static IList<IList<string>> ParseTableText(string value)
        {
            var rows = new List<IList<string>>();
            if (value == null)
            {
                return rows;
            }

            var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var row in normalized.Split(new[] { '\n', ';' }, StringSplitOptions.None))
            {
                rows.Add(row.Split('|').ToList());
            }

            return rows;
        }

        private static void UpdateTextMatrixStats(TableSplitResult result, IList<IList<string>> textRows, bool textSupplied)
        {
            if (!textSupplied)
            {
                result.MissingTextCells = 0;
                result.IgnoredTextCells = 0;
                return;
            }

            if (textRows == null || textRows.Count == 0)
            {
                result.MissingTextCells = result.RowSpan * result.ColumnSpan;
                return;
            }

            var supplied = textRows.Sum(row => row == null ? 0 : row.Count);
            var expected = result.RowSpan * result.ColumnSpan;
            result.MissingTextCells = Math.Max(0, expected - supplied);
            result.IgnoredTextCells = Math.Max(0, supplied - expected);
        }

        private static string TextAt(IList<IList<string>> rows, int row, int column, string defaultValue)
        {
            if (rows == null || row < 0 || column < 0 || row >= rows.Count)
            {
                return defaultValue ?? string.Empty;
            }

            var values = rows[row];
            if (values == null || column >= values.Count)
            {
                return defaultValue ?? string.Empty;
            }

            return values[column] ?? string.Empty;
        }

        private static void SetCellText(
            XElement cell,
            string text,
            HwpxTextStyleGuard textStyleGuard,
            ref long nextObjectId,
            TableSplitResult result)
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

        private static void SetCellAddress(XElement cell, int row, int column)
        {
            var address = EnsureCellChild(cell, "cellAddr");
            address.SetAttributeValue("rowAddr", row.ToString(CultureInfo.InvariantCulture));
            address.SetAttributeValue("colAddr", column.ToString(CultureInfo.InvariantCulture));
        }

        private static void SetCellSpan(XElement cell, int rowSpan, int columnSpan)
        {
            var span = EnsureCellChild(cell, "cellSpan");
            span.SetAttributeValue("rowSpan", rowSpan.ToString(CultureInfo.InvariantCulture));
            span.SetAttributeValue("colSpan", columnSpan.ToString(CultureInfo.InvariantCulture));
        }

        private static void SetCellSize(XElement cell, int width, int height)
        {
            var size = EnsureCellChild(cell, "cellSz");
            size.SetAttributeValue("width", Math.Max(1, width).ToString(CultureInfo.InvariantCulture));
            size.SetAttributeValue("height", Math.Max(1, height).ToString(CultureInfo.InvariantCulture));
        }

        private static XElement EnsureCellChild(XElement cell, string localName)
        {
            var child = cell.Element(Hp + localName);
            if (child != null)
            {
                return child;
            }

            child = new XElement(Hp + localName);
            cell.AddFirst(child);
            return child;
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

        private static long MaxNumericId(XElement scope)
        {
            if (scope == null)
            {
                return 0;
            }

            long max = 0;
            foreach (var attribute in scope.DescendantsAndSelf().Attributes("id"))
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

        private static void WriteReport(TableSplitResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Split Package Operation");
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
            AppendReportRow(report, "original physical cells", result.OriginalPhysicalCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new physical cells", result.NewPhysicalCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "added cells", result.AddedCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "missing text cells", result.MissingTextCells.ToString(CultureInfo.InvariantCulture));
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

    internal sealed class TableSplitOptions
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public int TableIndex { get; set; }

        public int Row { get; set; }

        public int Column { get; set; }

        public string Text { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class TableSplitResult
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

        public int OriginalPhysicalCells { get; set; }

        public int NewPhysicalCells { get; set; }

        public int AddedCells { get; set; }

        public int MissingTextCells { get; set; }

        public int IgnoredTextCells { get; set; }

        public int StyleRepairedRuns { get; set; }

        public string Note { get; set; }
    }
}
