using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxLayoutValidator
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static bool Validate(string templatePath, string candidatePath, string reportPath)
        {
            var template = ReadSection(templatePath);
            var candidate = ReadSection(candidatePath);
            var templateTables = ExtractTables(template).ToList();
            var candidateTables = ExtractTables(candidate).ToList();
            var issues = new List<string>();

            if (candidateTables.Count < templateTables.Count)
            {
                issues.Add(string.Format("FAIL: table count decreased: template={0}, candidate={1}", templateTables.Count, candidateTables.Count));
            }

            var comparableCount = Math.Min(templateTables.Count, candidateTables.Count);
            var changedCoreTables = 0;
            for (var index = 0; index < comparableCount; index++)
            {
                var original = templateTables[index];
                var current = candidateTables[index];
                var rowChanged = Math.Abs(current.RowCount - original.RowCount);
                var columnChanged = current.ColumnCount != original.ColumnCount;
                var widthChanged = Math.Abs(current.Width - original.Width) > 50;
                var borderChanged = !string.Equals(current.BorderFillId, original.BorderFillId, StringComparison.Ordinal);
                var labelChanged = original.FirstLabels.Count > 0 &&
                                   current.FirstLabels.Count > 0 &&
                                   !string.Equals(original.FirstLabels[0], current.FirstLabels[0], StringComparison.Ordinal);

                if (columnChanged || widthChanged || borderChanged || labelChanged)
                {
                    changedCoreTables++;
                    issues.Add(string.Format(
                        "FAIL: table {0} structure/style changed: rows {1}->{2}, cols {3}->{4}, width {5}->{6}, border {7}->{8}, first-label '{9}'->'{10}'",
                        index,
                        original.RowCount,
                        current.RowCount,
                        original.ColumnCount,
                        current.ColumnCount,
                        original.Width,
                        current.Width,
                        original.BorderFillId,
                        current.BorderFillId,
                        Abbreviate(original.FirstLabels.FirstOrDefault() ?? string.Empty),
                        Abbreviate(current.FirstLabels.FirstOrDefault() ?? string.Empty)));
                    continue;
                }

                if (rowChanged > Math.Max(3, original.RowCount))
                {
                    issues.Add(string.Format("WARN: table {0} row count changed heavily: {1}->{2}", index, original.RowCount, current.RowCount));
                }
            }

            var templateParagraphs = ExtractDirectParagraphs(template).ToList();
            var candidateParagraphs = ExtractDirectParagraphs(candidate).ToList();
            var paragraphStyleDrift = CountLeadingParagraphStyleDrift(templateParagraphs, candidateParagraphs, Math.Min(80, Math.Min(templateParagraphs.Count, candidateParagraphs.Count)));
            if (paragraphStyleDrift > 10)
            {
                issues.Add(string.Format("FAIL: leading paragraph style drift is too high: {0}", paragraphStyleDrift));
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Layout Validation");
            report.AppendLine();
            report.AppendLine(string.Format("- template: {0}", Path.GetFullPath(templatePath)));
            report.AppendLine(string.Format("- candidate: {0}", Path.GetFullPath(candidatePath)));
            report.AppendLine(string.Format("- template tables: {0}", templateTables.Count));
            report.AppendLine(string.Format("- candidate tables: {0}", candidateTables.Count));
            report.AppendLine(string.Format("- changed core tables: {0}", changedCoreTables));
            report.AppendLine(string.Format("- leading paragraph style drift: {0}", paragraphStyleDrift));
            report.AppendLine();
            report.AppendLine("## Issues");
            if (issues.Count == 0)
            {
                report.AppendLine("- PASS: no structural layout issues detected.");
            }
            else
            {
                foreach (var issue in issues)
                {
                    report.AppendLine("- " + issue);
                }
            }

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            }

            Console.Write(report.ToString());
            return !issues.Any(issue => issue.StartsWith("FAIL:", StringComparison.Ordinal));
        }

        private static XDocument ReadSection(string hwpxPath)
        {
            var entries = SimpleZipArchive.ReadAll(hwpxPath);
            if (!entries.ContainsKey("Contents/section0.xml"))
            {
                throw new InvalidOperationException("The HWPX file does not contain Contents/section0.xml: " + hwpxPath);
            }

            return XDocument.Parse(Encoding.UTF8.GetString(entries["Contents/section0.xml"]), LoadOptions.PreserveWhitespace);
        }

        private static IEnumerable<TableSignature> ExtractTables(XDocument document)
        {
            foreach (var table in document.Descendants(Hp + "tbl"))
            {
                var size = table.Element(Hp + "sz");
                yield return new TableSignature
                {
                    RowCount = GetInt(table, "rowCnt", table.Elements(Hp + "tr").Count()),
                    ColumnCount = GetInt(table, "colCnt", MaxColumnCount(table)),
                    Width = size == null ? 0 : GetInt(size, "width", 0),
                    BorderFillId = GetString(table, "borderFillIDRef"),
                    FirstLabels = table.Descendants(Hp + "tc")
                        .Take(8)
                        .Select(CellText)
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .ToList()
                };
            }
        }

        private static IEnumerable<ParagraphSignature> ExtractDirectParagraphs(XDocument document)
        {
            var root = document.Root;
            if (root == null)
            {
                yield break;
            }

            foreach (var paragraph in root.Elements(Hp + "p"))
            {
                if (paragraph.Descendants(Hp + "tbl").Any())
                {
                    yield return new ParagraphSignature
                    {
                        StyleId = GetString(paragraph, "styleIDRef"),
                        ParaPrId = GetString(paragraph, "paraPrIDRef"),
                        CharPrId = string.Empty
                    };
                    continue;
                }

                var run = paragraph.Element(Hp + "run");
                yield return new ParagraphSignature
                {
                    StyleId = GetString(paragraph, "styleIDRef"),
                    ParaPrId = GetString(paragraph, "paraPrIDRef"),
                    CharPrId = run == null ? string.Empty : GetString(run, "charPrIDRef")
                };
            }
        }

        private static int CountLeadingParagraphStyleDrift(IList<ParagraphSignature> template, IList<ParagraphSignature> candidate, int count)
        {
            var drift = 0;
            for (var index = 0; index < count; index++)
            {
                if (!string.Equals(template[index].StyleId, candidate[index].StyleId, StringComparison.Ordinal) ||
                    !string.Equals(template[index].ParaPrId, candidate[index].ParaPrId, StringComparison.Ordinal) ||
                    !string.Equals(template[index].CharPrId, candidate[index].CharPrId, StringComparison.Ordinal))
                {
                    drift++;
                }
            }

            return drift;
        }

        private static int MaxColumnCount(XElement table)
        {
            return table.Elements(Hp + "tr").Select(row => row.Elements(Hp + "tc").Count()).DefaultIfEmpty(0).Max();
        }

        private static string CellText(XElement cell)
        {
            var builder = new StringBuilder();
            foreach (var textNode in cell.Descendants(Hp + "t"))
            {
                builder.Append(textNode.Value);
            }

            return builder.ToString().Trim();
        }

        private static string GetString(XElement element, string attributeName)
        {
            var attribute = element.Attribute(attributeName);
            return attribute == null ? string.Empty : attribute.Value;
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

        private static string Abbreviate(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= 40)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, 37) + "...";
        }

        private sealed class TableSignature
        {
            public int RowCount { get; set; }

            public int ColumnCount { get; set; }

            public int Width { get; set; }

            public string BorderFillId { get; set; }

            public IList<string> FirstLabels { get; set; }
        }

        private sealed class ParagraphSignature
        {
            public string StyleId { get; set; }

            public string ParaPrId { get; set; }

            public string CharPrId { get; set; }
        }
    }
}
