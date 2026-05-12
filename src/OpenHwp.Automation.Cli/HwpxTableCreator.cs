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
    internal static class HwpxTableCreator
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";
        private static readonly XNamespace Hh = "http://www.hancom.co.kr/hwpml/2011/head";

        public static TableCreateResult Create(TableCreateOptions options)
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

            if (options.Rows <= 0)
            {
                throw new ArgumentException("rows must be greater than zero.", nameof(options));
            }

            if (options.Columns <= 0)
            {
                throw new ArgumentException("cols must be greater than zero.", nameof(options));
            }

            var result = new TableCreateResult
            {
                InputPath = Path.GetFullPath(options.InputPath),
                OutputPath = Path.GetFullPath(options.OutputPath),
                Section = NormalizeSection(options.Section),
                Rows = options.Rows,
                Columns = options.Columns,
                AnchorText = options.AnchorText ?? string.Empty,
                ReferenceTableIndex = options.ReferenceTableIndex,
                Note = "table was not created"
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
            result.OriginalTableCount = CountTopLevelTables(section);
            result.NewTableCount = result.OriginalTableCount;
            result.BorderFillId = options.BorderFillId;
            result.HeaderBorderFillId = options.HeaderBorderFillId;
            var tableText = ParseTableText(options.Text);
            UpdateTextMatrixStats(result, tableText);

            if (!ValidateBorderFillIds(entries, options, result))
            {
                WriteReport(result, options.ReportPath);
                return result;
            }

            var allCandidates = InspectReferenceCandidates(section, options.Columns);
            result.ReferenceCandidateCount = allCandidates.Count;
            result.UsableReferenceCandidateCount = allCandidates.Count(item => item.Usable);
            result.ReferenceCandidateSummary = FormatReferenceCandidates(allCandidates);

            var candidates = allCandidates.Where(item => item.Usable).ToList();
            if (candidates.Count == 0)
            {
                result.Note = "no unmerged top-level table without nested tables was found to use as a style reference";
                WriteReport(result, options.ReportPath);
                return result;
            }

            TableReferenceCandidate reference;
            if (options.ReferenceTableIndex.HasValue)
            {
                reference = candidates.FirstOrDefault(item => item.Index == options.ReferenceTableIndex.Value);
                if (reference == null)
                {
                    result.Note = "reference table index is not usable: " + options.ReferenceTableIndex.Value.ToString(CultureInfo.InvariantCulture);
                    WriteReport(result, options.ReportPath);
                    return result;
                }
            }
            else
            {
                reference = candidates
                    .OrderBy(item => item.Score)
                    .ThenBy(item => item.Index)
                    .First();
            }

            var nextObjectId = HwpxSectionPartResolver.NextBodyObjectId(entries);
            var paragraph = CreateTableParagraph(
                reference.Table,
                options.Rows,
                options.Columns,
                tableText,
                options.BorderFillId,
                options.HeaderBorderFillId,
                ref nextObjectId);

            if (paragraph == null)
            {
                result.Note = "failed to build table paragraph from reference table";
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.StyleRepairedRuns = textStyleGuard.EnsureMinimumCharHeight(paragraph);

            if (!InsertParagraph(section, paragraph, options.AnchorText, out var insertionNote))
            {
                result.Note = insertionNote;
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.ReferenceTableIndex = reference.Index;
            result.ReferenceRows = reference.Grid.RowCount;
            result.ReferenceColumns = reference.Grid.ColumnCount;
            result.InsertedParagraphId = (string)paragraph.Attribute("id") ?? string.Empty;
            result.InsertedAt = insertionNote;
            result.NewTableCount = CountTopLevelTables(section);
            if (result.NewTableCount != result.OriginalTableCount + 1)
            {
                result.Note = "created table was not inserted as a top-level table";
                WriteReport(result, options.ReportPath);
                return result;
            }

            result.Applied = true;
            result.Note = "table created by cloning reference table style";

            entries[sectionPart] = SerializeXml(section);
            UpdatePreviewText(entries);
            SimpleZipArchive.WriteAllPreservingTemplate(result.InputPath, result.OutputPath, entries);
            WriteReport(result, options.ReportPath);
            return result;
        }

        private static XElement CreateTableParagraph(
            XElement referenceTable,
            int rows,
            int columns,
            IList<IList<string>> tableText,
            int? borderFillId,
            int? headerBorderFillId,
            ref long nextObjectId)
        {
            var referenceParagraph = referenceTable.Ancestors(Hp + "p").FirstOrDefault();
            if (referenceParagraph == null)
            {
                return null;
            }

            var paragraph = new XElement(referenceParagraph.Name, referenceParagraph.Attributes());
            paragraph.SetAttributeValue("id", NextObjectId(ref nextObjectId));

            var referenceRun = referenceTable.Ancestors(Hp + "run").FirstOrDefault();
            var run = referenceRun == null
                ? new XElement(Hp + "run")
                : new XElement(referenceRun.Name, referenceRun.Attributes());
            var table = new XElement(referenceTable);
            run.Add(table);
            paragraph.Add(run);

            if (paragraph.Descendants(Hp + "tbl").Count() != 1)
            {
                return null;
            }

            table.SetAttributeValue("id", NextObjectId(ref nextObjectId));
            table.SetAttributeValue("rowCnt", rows.ToString(CultureInfo.InvariantCulture));
            table.SetAttributeValue("colCnt", columns.ToString(CultureInfo.InvariantCulture));

            var existingRows = table.Elements(Hp + "tr").ToList();
            if (existingRows.Count == 0)
            {
                return null;
            }

            var headerTemplate = existingRows[0];
            var bodyTemplate = existingRows.Count > 1 ? existingRows[1] : existingRows[0];
            var tableWidth = GetTableWidth(referenceTable);
            var columnWidths = BuildColumnWidths(referenceTable, columns, tableWidth);
            table.Elements(Hp + "tr").Remove();

            for (var rowIndex = 0; rowIndex < rows; rowIndex++)
            {
                var templateRow = rowIndex == 0 ? headerTemplate : bodyTemplate;
                table.Add(CreateTableRow(
                    templateRow,
                    rowIndex,
                    columns,
                    columnWidths,
                    tableText,
                    borderFillId,
                    headerBorderFillId,
                    ref nextObjectId));
            }

            UpdateTableSize(table);
            return paragraph;
        }

        private static XElement CreateTableRow(
            XElement templateRow,
            int rowIndex,
            int columns,
            IList<int> columnWidths,
            IList<IList<string>> tableText,
            int? borderFillId,
            int? headerBorderFillId,
            ref long nextObjectId)
        {
            var row = new XElement(templateRow);
            var templateCells = templateRow.Elements(Hp + "tc").ToList();
            row.Elements(Hp + "tc").Remove();

            for (var columnIndex = 0; columnIndex < columns; columnIndex++)
            {
                var templateCell = templateCells.Count == 0 ? null : templateCells[Math.Min(columnIndex, templateCells.Count - 1)];
                if (templateCell == null)
                {
                    continue;
                }

                row.Add(CreateTableCell(
                    templateCell,
                    rowIndex,
                    columnIndex,
                    columnWidths[Math.Min(columnIndex, columnWidths.Count - 1)],
                    TextAt(tableText, rowIndex, columnIndex),
                    borderFillId,
                    headerBorderFillId,
                    ref nextObjectId));
            }

            return row;
        }

        private static XElement CreateTableCell(
            XElement templateCell,
            int rowIndex,
            int columnIndex,
            int width,
            string text,
            int? borderFillId,
            int? headerBorderFillId,
            ref long nextObjectId)
        {
            var cell = new XElement(templateCell);
            EnsureCellChild(cell, "cellAddr").SetAttributeValue("rowAddr", rowIndex.ToString(CultureInfo.InvariantCulture));
            cell.Element(Hp + "cellAddr").SetAttributeValue("colAddr", columnIndex.ToString(CultureInfo.InvariantCulture));
            EnsureCellChild(cell, "cellSpan").SetAttributeValue("rowSpan", "1");
            cell.Element(Hp + "cellSpan").SetAttributeValue("colSpan", "1");
            EnsureCellChild(cell, "cellSz").SetAttributeValue("width", width.ToString(CultureInfo.InvariantCulture));

            if (rowIndex == 0 && headerBorderFillId.HasValue)
            {
                cell.SetAttributeValue("borderFillIDRef", headerBorderFillId.Value.ToString(CultureInfo.InvariantCulture));
            }
            else if (borderFillId.HasValue)
            {
                cell.SetAttributeValue("borderFillIDRef", borderFillId.Value.ToString(CultureInfo.InvariantCulture));
            }

            var paragraphs = HwpxTableModel.GetDirectCellParagraphs(cell)
                .Where(item => !item.Descendants(Hp + "tbl").Any())
                .ToList();
            if (paragraphs.Count > 0)
            {
                paragraphs[0].SetAttributeValue("id", NextObjectId(ref nextObjectId));
                SetParagraphText(paragraphs[0], text);
                paragraphs.Skip(1).Remove();
            }

            HwpxTextLayoutHelper.ExpandRowHeightForText(cell, text);
            return cell;
        }

        private static bool InsertParagraph(XDocument section, XElement paragraph, string anchorText, out string note)
        {
            if (section.Root == null)
            {
                note = "section XML has no root element";
                return false;
            }

            if (string.IsNullOrWhiteSpace(anchorText))
            {
                section.Root.Add(paragraph);
                note = "doc-end";
                return true;
            }

            foreach (var candidate in section.Root.Elements(Hp + "p"))
            {
                if (!ParagraphText(candidate).Contains(anchorText))
                {
                    continue;
                }

                candidate.AddAfterSelf(paragraph);
                note = "after-top-level-anchor";
                return true;
            }

            note = "top-level anchor text was not found: " + anchorText;
            return false;
        }

        private static IList<TableReferenceCandidate> InspectReferenceCandidates(XDocument section, int requestedColumns)
        {
            if (section == null)
            {
                return new List<TableReferenceCandidate>();
            }

            var result = new List<TableReferenceCandidate>();
            var tables = section.Descendants(Hp + "tbl")
                .Where(table => !table.Ancestors(Hp + "tbl").Any())
                .ToList();
            for (var index = 0; index < tables.Count; index++)
            {
                var table = tables[index];
                var candidate = new TableReferenceCandidate
                {
                    Index = index,
                    Table = table
                };

                if (HwpxTableModel.HasMergedCells(table))
                {
                    candidate.RejectionReason = "merged cells";
                    result.Add(candidate);
                    continue;
                }

                if (table.Elements(Hp + "tr").Count() == 0)
                {
                    candidate.RejectionReason = "no rows";
                    result.Add(candidate);
                    continue;
                }

                if (HwpxTableModel.DirectCells(table).Any(cell => cell.Descendants(Hp + "tbl").Any()))
                {
                    candidate.RejectionReason = "nested table";
                    result.Add(candidate);
                    continue;
                }

                var grid = HwpxTableModel.BuildGrid(table);
                if (grid.ColumnCount <= 0 || grid.RowCount <= 0 || grid.OverlapIssues.Count > 0)
                {
                    candidate.Grid = grid;
                    candidate.RejectionReason = grid.OverlapIssues.Count > 0 ? "grid overlap" : "empty grid";
                    result.Add(candidate);
                    continue;
                }

                candidate.Grid = grid;
                candidate.Score = ScoreReference(grid.ColumnCount, grid.RowCount, requestedColumns);
                candidate.Usable = true;
                result.Add(candidate);
            }

            return result;
        }

        private static bool ValidateBorderFillIds(IDictionary<string, byte[]> entries, TableCreateOptions options, TableCreateResult result)
        {
            if (!options.BorderFillId.HasValue && !options.HeaderBorderFillId.HasValue)
            {
                return true;
            }

            var ids = ReadBorderFillIds(entries);
            if (ids.Count == 0)
            {
                result.Note = "border fill definitions were not found in Contents/header.xml";
                return false;
            }

            if (options.BorderFillId.HasValue && !ids.Contains(options.BorderFillId.Value))
            {
                result.Note = "border fill id was not found: " + options.BorderFillId.Value.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (options.HeaderBorderFillId.HasValue && !ids.Contains(options.HeaderBorderFillId.Value))
            {
                result.Note = "header border fill id was not found: " + options.HeaderBorderFillId.Value.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            return true;
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

        private static int ScoreReference(int columns, int rows, int requestedColumns)
        {
            var score = Math.Abs(columns - requestedColumns) * 20;
            if (columns < requestedColumns)
            {
                score += 50;
            }

            score += Math.Min(20, rows);
            return score;
        }

        private static IList<int> BuildColumnWidths(XElement referenceTable, int columnCount, int tableWidth)
        {
            var firstRow = referenceTable.Elements(Hp + "tr").FirstOrDefault();
            var referenceWidths = firstRow == null
                ? new List<int>()
                : firstRow.Elements(Hp + "tc")
                    .Select(cell =>
                    {
                        var size = cell.Element(Hp + "cellSz");
                        return size == null ? 0 : GetInt(size, "width", 0);
                    })
                    .Where(width => width > 0)
                    .ToList();

            if (referenceWidths.Count == columnCount)
            {
                return referenceWidths;
            }

            var width = tableWidth > 0 ? tableWidth : 45000;
            var baseWidth = Math.Max(1000, width / Math.Max(1, columnCount));
            var widths = Enumerable.Repeat(baseWidth, columnCount).ToList();
            var remainder = width - (baseWidth * columnCount);
            if (widths.Count > 0)
            {
                widths[widths.Count - 1] += remainder;
            }

            return widths;
        }

        private static void UpdateTableSize(XElement table)
        {
            if (table == null)
            {
                return;
            }

            var rows = table.Elements(Hp + "tr").ToList();
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

            var sizeElement = table.Element(Hp + "sz");
            if (sizeElement != null)
            {
                if (width > 0)
                {
                    sizeElement.SetAttributeValue("width", width.ToString(CultureInfo.InvariantCulture));
                }

                if (height > 0)
                {
                    sizeElement.SetAttributeValue("height", height.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private static int GetTableWidth(XElement table)
        {
            var size = table == null ? null : table.Element(Hp + "sz");
            if (size != null)
            {
                var width = GetInt(size, "width", 0);
                if (width > 0)
                {
                    return width;
                }
            }

            var firstRow = table == null ? null : table.Elements(Hp + "tr").FirstOrDefault();
            if (firstRow == null)
            {
                return 0;
            }

            return firstRow.Elements(Hp + "tc")
                .Select(cell =>
                {
                    var cellSize = cell.Element(Hp + "cellSz");
                    return cellSize == null ? 0 : GetInt(cellSize, "width", 0);
                })
                .Sum();
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

        private static void UpdateTextMatrixStats(TableCreateResult result, IList<IList<string>> rows)
        {
            if (result == null)
            {
                return;
            }

            var sourceRows = rows ?? new List<IList<string>>();
            result.TextRows = sourceRows.Count;
            result.TextMaxColumns = sourceRows.Select(row => row == null ? 0 : row.Count).DefaultIfEmpty(0).Max();

            var ignoredCells = 0;
            var missingCells = 0;
            for (var rowIndex = 0; rowIndex < result.Rows; rowIndex++)
            {
                var row = rowIndex < sourceRows.Count ? sourceRows[rowIndex] : null;
                var sourceColumns = row == null ? 0 : row.Count;
                if (sourceColumns > result.Columns)
                {
                    ignoredCells += sourceColumns - result.Columns;
                }

                if (sourceColumns < result.Columns)
                {
                    missingCells += result.Columns - sourceColumns;
                }
            }

            for (var rowIndex = result.Rows; rowIndex < sourceRows.Count; rowIndex++)
            {
                result.IgnoredTextRows++;
                ignoredCells += sourceRows[rowIndex] == null ? 0 : sourceRows[rowIndex].Count;
            }

            result.IgnoredTextCells = ignoredCells;
            result.MissingTextCells = missingCells;
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

        private static string ParagraphText(XElement paragraph)
        {
            return HwpxTableModel.TextOfDirectParagraph(paragraph) ?? string.Empty;
        }

        private static string FormatReferenceCandidates(IList<TableReferenceCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(
                "; ",
                candidates.Take(12).Select(candidate =>
                {
                    var grid = candidate.Grid;
                    var size = grid == null
                        ? "?x?"
                        : grid.RowCount.ToString(CultureInfo.InvariantCulture) + "x" + grid.ColumnCount.ToString(CultureInfo.InvariantCulture);
                    return candidate.Usable
                        ? "#" + candidate.Index.ToString(CultureInfo.InvariantCulture) + " usable " + size
                        : "#" + candidate.Index.ToString(CultureInfo.InvariantCulture) + " rejected " + (candidate.RejectionReason ?? "unknown");
                }).ToArray());
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
                    var text = ParagraphText(paragraph).Trim();
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    builder.AppendLine(text);
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

        private static void WriteReport(TableCreateResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Table Create Package Writer");
            report.AppendLine();
            report.AppendLine("- input: `" + result.InputPath + "`");
            report.AppendLine("- output: `" + result.OutputPath + "`");
            report.AppendLine("- section: `" + result.Section + "`");
            report.AppendLine("- verdict: " + (result.Applied ? "applied" : "failed"));
            report.AppendLine();
            report.AppendLine("| field | value |");
            report.AppendLine("| --- | --- |");
            AppendReportRow(report, "rows", result.Rows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "columns", result.Columns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "original top-level table count", result.OriginalTableCount.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "new top-level table count", result.NewTableCount.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "reference table index", result.ReferenceTableIndex.HasValue ? result.ReferenceTableIndex.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendReportRow(report, "reference rows", result.ReferenceRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "reference columns", result.ReferenceColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "reference candidates", result.ReferenceCandidateCount.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "usable reference candidates", result.UsableReferenceCandidateCount.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "reference candidate summary", result.ReferenceCandidateSummary);
            AppendReportRow(report, "border fill id", result.BorderFillId.HasValue ? result.BorderFillId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendReportRow(report, "header border fill id", result.HeaderBorderFillId.HasValue ? result.HeaderBorderFillId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            AppendReportRow(report, "text rows", result.TextRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "text max columns", result.TextMaxColumns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "missing text cells", result.MissingTextCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "ignored text rows", result.IgnoredTextRows.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "ignored text cells", result.IgnoredTextCells.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "style repaired runs", result.StyleRepairedRuns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "inserted paragraph id", result.InsertedParagraphId);
            AppendReportRow(report, "inserted at", result.InsertedAt);
            AppendReportRow(report, "anchor", result.AnchorText);
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

        private sealed class TableReferenceCandidate
        {
            public int Index { get; set; }

            public XElement Table { get; set; }

            public TableGrid Grid { get; set; }

            public int Score { get; set; }

            public bool Usable { get; set; }

            public string RejectionReason { get; set; }
        }
    }

    internal sealed class TableCreateOptions
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public int Rows { get; set; }

        public int Columns { get; set; }

        public string Text { get; set; }

        public string Section { get; set; }

        public string AnchorText { get; set; }

        public int? ReferenceTableIndex { get; set; }

        public int? BorderFillId { get; set; }

        public int? HeaderBorderFillId { get; set; }

        public string ReportPath { get; set; }
    }

    internal sealed class TableCreateResult
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Section { get; set; }

        public bool Applied { get; set; }

        public int Rows { get; set; }

        public int Columns { get; set; }

        public int OriginalTableCount { get; set; }

        public int NewTableCount { get; set; }

        public int? ReferenceTableIndex { get; set; }

        public int ReferenceRows { get; set; }

        public int ReferenceColumns { get; set; }

        public int ReferenceCandidateCount { get; set; }

        public int UsableReferenceCandidateCount { get; set; }

        public string ReferenceCandidateSummary { get; set; }

        public int? BorderFillId { get; set; }

        public int? HeaderBorderFillId { get; set; }

        public int TextRows { get; set; }

        public int TextMaxColumns { get; set; }

        public int MissingTextCells { get; set; }

        public int IgnoredTextRows { get; set; }

        public int IgnoredTextCells { get; set; }

        public int StyleRepairedRuns { get; set; }

        public string InsertedParagraphId { get; set; }

        public string InsertedAt { get; set; }

        public string AnchorText { get; set; }

        public string Note { get; set; }
    }
}
