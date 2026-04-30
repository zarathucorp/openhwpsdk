using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class MarkdownHwpxWriter
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";
        private static readonly Regex HeadingPattern = new Regex(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex MarkdownImagePattern = new Regex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex MarkdownLinkPattern = new Regex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex HtmlBreakPattern = new Regex(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HorizontalRulePattern = new Regex(@"^-{3,}$", RegexOptions.Compiled);
        private static readonly Regex WhitespacePattern = new Regex(@"[ \t]+", RegexOptions.Compiled);

        public static void Convert(string markdownPath, string templatePath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(markdownPath))
            {
                throw new ArgumentException("Markdown path is required.", nameof(markdownPath));
            }

            if (string.IsNullOrWhiteSpace(templatePath))
            {
                throw new ArgumentException("Template HWPX path is required.", nameof(templatePath));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output HWPX path is required.", nameof(outputPath));
            }

            var blocks = ParseMarkdown(ReadTextFile(markdownPath));
            var entries = SimpleZipArchive.ReadAll(templatePath);
            if (!entries.ContainsKey("Contents/section0.xml"))
            {
                throw new InvalidOperationException("The template HWPX does not contain Contents/section0.xml.");
            }

            var sectionXml = Encoding.UTF8.GetString(entries["Contents/section0.xml"]);
            var document = XDocument.Parse(sectionXml, LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root == null)
            {
                throw new InvalidOperationException("The template section XML has no root element.");
            }

            RewriteSection(root, blocks);
            entries["Contents/section0.xml"] = Encoding.UTF8.GetBytes(document.Declaration + document.ToString(SaveOptions.DisableFormatting));
            entries["Preview/PrvText.txt"] = Encoding.UTF8.GetBytes(BuildPreviewText(blocks));

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SimpleZipArchive.WriteAll(outputPath, entries);
        }

        private static void RewriteSection(XElement root, IList<MarkdownBlock> blocks)
        {
            var directParagraphs = root.Elements(Hp + "p").ToList();
            if (directParagraphs.Count == 0)
            {
                throw new InvalidOperationException("The template section has no paragraph templates.");
            }

            var firstParagraph = new XElement(directParagraphs[0]);
            var h1Template = CleanParagraphClone(FindParagraph(root, "2026년 기술사업화 패키지 사업계획서 (본문2)", directParagraphs[0]));
            var h2Template = CleanParagraphClone(FindParagraph(root, "1) 수행기업 인력 등 현황", directParagraphs[0]));
            var h3Template = CleanParagraphClone(FindParagraph(root, "1-1. 사업화 대상 기술 정의 및 주요 특성", directParagraphs[0]));
            var bodyTemplate = CleanParagraphClone(FindParagraph(root, "◦", directParagraphs[0]));
            var tableTemplate = PrepareTableTemplate(root);

            foreach (var paragraph in directParagraphs)
            {
                paragraph.Remove();
            }

            var startIndex = 0;
            if (blocks.Count > 0 && blocks[0].Kind == MarkdownBlockKind.Heading)
            {
                SetText(firstParagraph, blocks[0].Text);
                startIndex = 1;
            }
            else
            {
                SetText(firstParagraph, "Markdown imported document");
            }

            RemoveLineSegments(firstParagraph);
            root.Add(firstParagraph);

            var tableId = 900000001;
            for (var index = startIndex; index < blocks.Count; index++)
            {
                var block = blocks[index];
                if (block.Kind == MarkdownBlockKind.Blank)
                {
                    root.Add(CreateParagraph(bodyTemplate, string.Empty));
                    continue;
                }

                if (block.Kind == MarkdownBlockKind.Heading)
                {
                    var template = block.Level <= 1 ? h1Template : block.Level == 2 ? h2Template : h3Template;
                    root.Add(CreateParagraph(template, block.Text));
                    continue;
                }

                if (block.Kind == MarkdownBlockKind.Paragraph)
                {
                    if (!string.IsNullOrWhiteSpace(block.Text))
                    {
                        root.Add(CreateParagraph(bodyTemplate, block.Text));
                    }

                    continue;
                }

                if (block.Kind == MarkdownBlockKind.Table)
                {
                    root.Add(CreateTable(tableTemplate, block.Rows, tableId));
                    tableId++;
                }
            }
        }

        private static XElement FindParagraph(XElement root, string text, XElement fallback)
        {
            foreach (var paragraph in root.Elements(Hp + "p"))
            {
                if (!HasTable(paragraph) && string.Equals(DirectText(paragraph), text, StringComparison.Ordinal))
                {
                    return paragraph;
                }
            }

            return fallback;
        }

        private static XElement CleanParagraphClone(XElement paragraph)
        {
            var clone = new XElement(paragraph);
            foreach (var table in clone.Descendants(Hp + "tbl").ToList())
            {
                table.Remove();
            }

            RemoveLineSegments(clone);
            return clone;
        }

        private static XElement CreateParagraph(XElement template, string text)
        {
            var paragraph = new XElement(template);
            SetText(paragraph, text ?? string.Empty);
            RemoveLineSegments(paragraph);
            return paragraph;
        }

        private static XElement PrepareTableTemplate(XElement root)
        {
            foreach (var paragraph in root.Elements(Hp + "p"))
            {
                var table = paragraph.Descendants(Hp + "tbl").FirstOrDefault();
                if (table == null)
                {
                    continue;
                }

                var rows = table.Elements(Hp + "tr").ToList();
                if (rows.Count < 2)
                {
                    continue;
                }

                var firstCells = rows[0].Elements(Hp + "tc").ToList();
                var secondCells = rows[1].Elements(Hp + "tc").ToList();
                if (firstCells.Count < 2 || firstCells.Count > 8 || firstCells.Count != secondCells.Count)
                {
                    continue;
                }

                if (AllCellsAreSingleSpan(firstCells) && AllCellsAreSingleSpan(secondCells))
                {
                    return new XElement(paragraph);
                }
            }

            throw new InvalidOperationException("A simple table template was not found in the HWPX file.");
        }

        private static bool AllCellsAreSingleSpan(IEnumerable<XElement> cells)
        {
            foreach (var cell in cells)
            {
                var span = cell.Element(Hp + "cellSpan");
                if (span == null)
                {
                    continue;
                }

                if (GetInt(span, "colSpan", 1) != 1 || GetInt(span, "rowSpan", 1) != 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static XElement CreateTable(XElement tableParagraphTemplate, IList<IList<string>> rows, int tableId)
        {
            var paragraph = new XElement(tableParagraphTemplate);
            var table = paragraph.Descendants(Hp + "tbl").FirstOrDefault();
            if (table == null)
            {
                throw new InvalidOperationException("Table paragraph template does not contain a table.");
            }

            var existingRows = table.Elements(Hp + "tr").ToList();
            var headerPrototype = existingRows[0].Elements(Hp + "tc").First();
            var bodyPrototype = existingRows[Math.Min(1, existingRows.Count - 1)].Elements(Hp + "tc").First();
            var bodySize = bodyPrototype.Element(Hp + "cellSz") ?? headerPrototype.Element(Hp + "cellSz");
            var rowHeight = bodySize == null ? 1100 : GetInt(bodySize, "height", 1100);

            foreach (var row in existingRows)
            {
                row.Remove();
            }

            var columnCount = Math.Max(1, rows.Max(row => row.Count));
            var rowCount = Math.Max(1, rows.Count);
            table.SetAttributeValue("id", tableId.ToString());
            table.SetAttributeValue("rowCnt", rowCount.ToString());
            table.SetAttributeValue("colCnt", columnCount.ToString());

            var tableSize = table.Element(Hp + "sz");
            var totalWidth = tableSize == null ? 46208 : GetInt(tableSize, "width", 46208);
            var columnWidth = Math.Max(1200, totalWidth / columnCount);
            if (tableSize != null)
            {
                tableSize.SetAttributeValue("height", Math.Max(rowHeight, rowHeight * rowCount).ToString());
            }

            for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var rowElement = new XElement(Hp + "tr");
                var rowValues = rows[rowIndex];
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    var prototype = rowIndex == 0 ? headerPrototype : bodyPrototype;
                    var cell = new XElement(prototype);
                    UpdateCellGeometry(cell, rowIndex, columnIndex, columnWidth, rowHeight);
                    SetText(cell, columnIndex < rowValues.Count ? rowValues[columnIndex].Replace("\n", " / ") : string.Empty);
                    RemoveLineSegments(cell);
                    rowElement.Add(cell);
                }

                table.Add(rowElement);
            }

            return paragraph;
        }

        private static void UpdateCellGeometry(XElement cell, int rowIndex, int columnIndex, int columnWidth, int rowHeight)
        {
            var address = cell.Element(Hp + "cellAddr");
            if (address != null)
            {
                address.SetAttributeValue("rowAddr", rowIndex.ToString());
                address.SetAttributeValue("colAddr", columnIndex.ToString());
            }

            var span = cell.Element(Hp + "cellSpan");
            if (span != null)
            {
                span.SetAttributeValue("rowSpan", "1");
                span.SetAttributeValue("colSpan", "1");
            }

            var size = cell.Element(Hp + "cellSz");
            if (size != null)
            {
                size.SetAttributeValue("width", columnWidth.ToString());
                size.SetAttributeValue("height", rowHeight.ToString());
            }
        }

        private static void SetText(XElement element, string text)
        {
            var textNodes = element.Descendants(Hp + "t").ToList();
            if (textNodes.Count == 0)
            {
                return;
            }

            textNodes[0].Value = text ?? string.Empty;
            for (var index = 1; index < textNodes.Count; index++)
            {
                textNodes[index].Value = string.Empty;
            }
        }

        private static void RemoveLineSegments(XElement element)
        {
            foreach (var lineSegmentArray in element.Descendants(Hp + "linesegarray").ToList())
            {
                lineSegmentArray.Remove();
            }
        }

        private static bool HasTable(XElement paragraph)
        {
            return paragraph.Descendants(Hp + "tbl").Any();
        }

        private static string DirectText(XElement paragraph)
        {
            var builder = new StringBuilder();
            foreach (var run in paragraph.Elements(Hp + "run"))
            {
                if (run.Element(Hp + "tbl") != null)
                {
                    continue;
                }

                foreach (var textNode in run.Descendants(Hp + "t"))
                {
                    builder.Append(textNode.Value);
                }
            }

            return builder.ToString().Trim();
        }

        private static IList<MarkdownBlock> ParseMarkdown(string markdown)
        {
            var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var blocks = new List<MarkdownBlock>();
            var index = 0;

            while (index < lines.Length)
            {
                var raw = lines[index] ?? string.Empty;
                var trimmed = raw.Trim();
                if (trimmed.Length == 0 || HorizontalRulePattern.IsMatch(trimmed))
                {
                    AddBlank(blocks);
                    index++;
                    continue;
                }

                if (trimmed.StartsWith("|", StringComparison.Ordinal))
                {
                    var rows = new List<IList<string>>();
                    while (index < lines.Length && lines[index].Trim().StartsWith("|", StringComparison.Ordinal))
                    {
                        var tableLine = lines[index].Trim().Trim('|');
                        if (tableLine.Length > 0 && !IsMarkdownTableSeparator(tableLine))
                        {
                            rows.Add(tableLine.Split('|').Select(NormalizeInline).ToList());
                        }

                        index++;
                    }

                    if (rows.Count > 0)
                    {
                        blocks.Add(MarkdownBlock.Table(rows));
                    }

                    continue;
                }

                var heading = HeadingPattern.Match(trimmed);
                if (heading.Success)
                {
                    blocks.Add(MarkdownBlock.Heading(heading.Groups[1].Value.Length, NormalizeInline(heading.Groups[2].Value)));
                    index++;
                    continue;
                }

                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    while (index < lines.Length && lines[index].Trim().StartsWith(">", StringComparison.Ordinal))
                    {
                        var quote = NormalizeInline(lines[index].Trim().TrimStart('>').Trim());
                        if (quote.Length > 0)
                        {
                            blocks.Add(MarkdownBlock.Paragraph("※ " + quote));
                        }

                        index++;
                    }

                    continue;
                }

                blocks.Add(MarkdownBlock.Paragraph(NormalizeInline(raw)));
                index++;
            }

            while (blocks.Count > 0 && blocks[blocks.Count - 1].Kind == MarkdownBlockKind.Blank)
            {
                blocks.RemoveAt(blocks.Count - 1);
            }

            return blocks;
        }

        private static bool IsMarkdownTableSeparator(string tableLine)
        {
            foreach (var character in tableLine)
            {
                if (character != '-' && character != ':' && character != '|' && character != ' ')
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddBlank(IList<MarkdownBlock> blocks)
        {
            if (blocks.Count == 0 || blocks[blocks.Count - 1].Kind == MarkdownBlockKind.Blank)
            {
                return;
            }

            blocks.Add(MarkdownBlock.Blank());
        }

        private static string NormalizeInline(string text)
        {
            var normalized = HttpUtility.HtmlDecode(text ?? string.Empty);
            normalized = normalized.Replace("&nbsp;", " ");
            normalized = HtmlBreakPattern.Replace(normalized, "\n");
            normalized = MarkdownImagePattern.Replace(normalized, "[이미지: $1] $2");
            normalized = MarkdownLinkPattern.Replace(normalized, "$1 ($2)");
            normalized = normalized.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
            normalized = Regex.Replace(normalized, @"(?<!\*)\*(?!\*)", string.Empty);
            normalized = Regex.Replace(normalized, @"(?<!_)_(?!_)", string.Empty);
            return WhitespacePattern.Replace(normalized, " ").Trim();
        }

        private static string BuildPreviewText(IEnumerable<MarkdownBlock> blocks)
        {
            var builder = new StringBuilder();
            foreach (var block in blocks)
            {
                if (block.Kind == MarkdownBlockKind.Blank)
                {
                    builder.AppendLine();
                    continue;
                }

                if (block.Kind == MarkdownBlockKind.Heading || block.Kind == MarkdownBlockKind.Paragraph)
                {
                    builder.AppendLine(block.Text);
                    continue;
                }

                if (block.Kind == MarkdownBlockKind.Table)
                {
                    foreach (var row in block.Rows)
                    {
                        builder.Append('<');
                        builder.Append(string.Join("><", row.ToArray()));
                        builder.AppendLine(">");
                    }

                    builder.AppendLine();
                }
            }

            return builder.ToString().TrimEnd() + Environment.NewLine;
        }

        private static int GetInt(XElement element, string attributeName, int defaultValue)
        {
            var attribute = element.Attribute(attributeName);
            if (attribute == null)
            {
                return defaultValue;
            }

            int value;
            return int.TryParse(attribute.Value, out value) ? value : defaultValue;
        }

        private static string ReadTextFile(string path)
        {
            try
            {
                return File.ReadAllText(path, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException)
            {
                return File.ReadAllText(path, Encoding.Default);
            }
        }

        private enum MarkdownBlockKind
        {
            Blank,
            Heading,
            Paragraph,
            Table
        }

        private sealed class MarkdownBlock
        {
            private MarkdownBlock(MarkdownBlockKind kind)
            {
                Kind = kind;
                Rows = new List<IList<string>>();
            }

            public MarkdownBlockKind Kind { get; private set; }

            public int Level { get; private set; }

            public string Text { get; private set; }

            public IList<IList<string>> Rows { get; private set; }

            public static MarkdownBlock Blank()
            {
                return new MarkdownBlock(MarkdownBlockKind.Blank);
            }

            public static MarkdownBlock Heading(int level, string text)
            {
                return new MarkdownBlock(MarkdownBlockKind.Heading) { Level = level, Text = text };
            }

            public static MarkdownBlock Paragraph(string text)
            {
                return new MarkdownBlock(MarkdownBlockKind.Paragraph) { Text = text };
            }

            public static MarkdownBlock Table(IList<IList<string>> rows)
            {
                return new MarkdownBlock(MarkdownBlockKind.Table) { Rows = rows };
            }
        }
    }
}
