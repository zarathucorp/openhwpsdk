using System;
using System.Collections.Generic;
using System.Globalization;
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
            return Validate(templatePath, candidatePath, reportPath, new ValidationOptions());
        }

        public static bool Validate(string templatePath, string candidatePath, string reportPath, ValidationOptions options)
        {
            var template = ReadSection(templatePath);
            var candidate = ReadSection(candidatePath);
            var templateTables = ExtractTables(template).ToList();
            var candidateTables = ExtractTables(candidate).ToList();
            var issues = new List<LayoutIssue>();
            options = options ?? new ValidationOptions();

            if (candidateTables.Count < templateTables.Count)
            {
                issues.Add(LayoutIssue.Blocking(string.Format("table count decreased: template={0}, candidate={1}", templateTables.Count, candidateTables.Count)));
            }

            var changedCoreTables = 0;
            var candidateIndex = 0;
            for (var index = 0; index < templateTables.Count; index++)
            {
                var original = templateTables[index];
                var allowRowChange = options.AllowedRowGrowthTables.Contains(index);
                var allowColumnChange = options.AllowedColumnChangeTables.Contains(index);
                var matchedIndex = allowColumnChange
                    ? FindAllowedColumnChangeTable(candidateTables, original, candidateIndex, allowRowChange)
                    : FindMatchingCoreTable(candidateTables, original, candidateIndex);
                if (matchedIndex < 0)
                {
                    changedCoreTables++;
                    issues.Add(LayoutIssue.Blocking(string.Format(
                        "template table {0} was not preserved in candidate order: cols {1}, width {2}, border {3}, first-label '{4}'",
                        index,
                        original.ColumnCount,
                        original.Width,
                        original.BorderFillId,
                        Abbreviate(original.FirstLabels.FirstOrDefault() ?? string.Empty))));
                    continue;
                }

                if (matchedIndex > candidateIndex)
                {
                    issues.Add(LayoutIssue.ReviewNeeded(string.Format("{0} inserted table(s) before template table {1}", matchedIndex - candidateIndex, index)));
                }

                var current = candidateTables[matchedIndex];
                candidateIndex = matchedIndex + 1;
                var rowChanged = Math.Abs(current.RowCount - original.RowCount);
                var columnChanged = current.ColumnCount != original.ColumnCount || Math.Abs(current.Width - original.Width) > 50;

                foreach (var overlapIssue in current.GridOverlapIssues)
                {
                    issues.Add(LayoutIssue.Blocking(string.Format(
                        "table {0} has overlapping cell spans: {1}",
                        index,
                        overlapIssue)));
                }

                if (rowChanged > 0)
                {
                    var heavyChange = rowChanged > Math.Max(3, original.RowCount);
                    var message = heavyChange
                        ? string.Format("table {0} row count changed heavily: {1}->{2}", index, original.RowCount, current.RowCount)
                        : string.Format("table {0} row count changed: {1}->{2}", index, original.RowCount, current.RowCount);
                    issues.Add(options.AllowedRowGrowthTables.Contains(index)
                        ? LayoutIssue.ExpectedChange(message)
                        : LayoutIssue.ReviewNeeded(message));
                }

                if (columnChanged)
                {
                    var message = string.Format(
                        CultureInfo.InvariantCulture,
                        "table {0} column/width changed: cols {1}->{2}, width {3}->{4}",
                        index,
                        original.ColumnCount,
                        current.ColumnCount,
                        original.Width,
                        current.Width);
                    issues.Add(allowColumnChange
                        ? LayoutIssue.ExpectedChange(message)
                        : LayoutIssue.Blocking(message));
                }
            }

            if (candidateIndex < candidateTables.Count)
            {
                issues.Add(LayoutIssue.ReviewNeeded(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} extra table(s) after matched template tables",
                    candidateTables.Count - candidateIndex)));
            }

            var templateParagraphs = ExtractDirectParagraphs(template).ToList();
            var candidateParagraphs = ExtractDirectParagraphs(candidate).ToList();
            var paragraphStyleDrift = CountLeadingParagraphStyleDrift(templateParagraphs, candidateParagraphs, Math.Min(80, Math.Min(templateParagraphs.Count, candidateParagraphs.Count)));
            if (paragraphStyleDrift > options.MaxLeadingParagraphStyleDrift)
            {
                issues.Add(LayoutIssue.Blocking(string.Format(
                    CultureInfo.InvariantCulture,
                    "leading paragraph style drift is too high: {0} (max {1})",
                    paragraphStyleDrift,
                    options.MaxLeadingParagraphStyleDrift)));
            }

            var expectedCount = issues.Count(issue => string.Equals(issue.Severity, LayoutIssue.ExpectedChangeSeverity, StringComparison.Ordinal));
            var reviewCount = issues.Count(issue => string.Equals(issue.Severity, LayoutIssue.ReviewNeededSeverity, StringComparison.Ordinal));
            var blockingCount = issues.Count(issue => string.Equals(issue.Severity, LayoutIssue.BlockingSeverity, StringComparison.Ordinal));
            var verdict = blockingCount > 0 ? "blocking" : (reviewCount > 0 ? "review-needed" : "pass");

            var report = new StringBuilder();
            report.AppendLine("# HWPX Layout Validation");
            report.AppendLine();
            report.AppendLine(string.Format("- template: {0}", Path.GetFullPath(templatePath)));
            report.AppendLine(string.Format("- candidate: {0}", Path.GetFullPath(candidatePath)));
            report.AppendLine(string.Format("- template tables: {0}", templateTables.Count));
            report.AppendLine(string.Format("- candidate tables: {0}", candidateTables.Count));
            report.AppendLine(string.Format("- changed core tables: {0}", changedCoreTables));
            report.AppendLine(string.Format("- leading paragraph style drift: {0}", paragraphStyleDrift));
            report.AppendLine(string.Format("- allowed row-growth tables: {0}", options.AllowedRowGrowthTables.Count == 0 ? "none" : string.Join(", ", options.AllowedRowGrowthTables.OrderBy(item => item).Select(item => item.ToString(CultureInfo.InvariantCulture)).ToArray())));
            report.AppendLine(string.Format("- allowed column-change tables: {0}", options.AllowedColumnChangeTables.Count == 0 ? "none" : string.Join(", ", options.AllowedColumnChangeTables.OrderBy(item => item).Select(item => item.ToString(CultureInfo.InvariantCulture)).ToArray())));
            report.AppendLine(string.Format("- max leading paragraph style drift: {0}", options.MaxLeadingParagraphStyleDrift));
            report.AppendLine(string.Format("- expected-change issues: {0}", expectedCount));
            report.AppendLine(string.Format("- review-needed issues: {0}", reviewCount));
            report.AppendLine(string.Format("- blocking issues: {0}", blockingCount));
            report.AppendLine(string.Format("- verdict: {0}", verdict));
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
                    report.AppendLine("- " + issue.Severity + ": " + issue.Message);
                }
            }

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));
            }

            Console.Write(report.ToString());
            return blockingCount == 0;
        }

        private static int FindMatchingCoreTable(IList<TableSignature> candidateTables, TableSignature original, int startIndex)
        {
            for (var index = startIndex; index < candidateTables.Count; index++)
            {
                if (IsCoreTableMatch(original, candidateTables[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        private static int FindAllowedColumnChangeTable(IList<TableSignature> candidateTables, TableSignature original, int startIndex, bool allowRowChange)
        {
            for (var index = startIndex; index < candidateTables.Count; index++)
            {
                if (IsAllowedColumnChangeMatch(original, candidateTables[index], allowRowChange))
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool IsCoreTableMatch(TableSignature original, TableSignature current)
        {
            if (current.ColumnCount != original.ColumnCount)
            {
                return false;
            }

            if (Math.Abs(current.Width - original.Width) > 50)
            {
                return false;
            }

            return IsSameTableIdentity(original, current);
        }

        private static bool IsAllowedColumnChangeMatch(TableSignature original, TableSignature current, bool allowRowChange)
        {
            if (IsCoreTableMatch(original, current))
            {
                return true;
            }

            return (allowRowChange || original.RowCount == current.RowCount) &&
                   HasColumnOrWidthChange(original, current) &&
                   string.Equals(current.BorderFillId, original.BorderFillId, StringComparison.Ordinal) &&
                   HasStableLabelMatch(original, current);
        }

        private static bool HasColumnOrWidthChange(TableSignature original, TableSignature current)
        {
            return current.ColumnCount != original.ColumnCount ||
                   Math.Abs(current.Width - original.Width) > 50;
        }

        private static bool HasStableLabelMatch(TableSignature original, TableSignature current)
        {
            if (original.FirstLabels.Count == 0)
            {
                return true;
            }

            if (current.FirstLabels.Count == 0)
            {
                return false;
            }

            if (string.Equals(original.FirstLabels[0], current.FirstLabels[0], StringComparison.Ordinal))
            {
                return true;
            }

            var currentLabels = new HashSet<string>(current.FirstLabels, StringComparer.Ordinal);
            var overlap = original.FirstLabels.Count(label => currentLabels.Contains(label));
            var requiredOverlap = Math.Min(2, original.FirstLabels.Count);
            return overlap >= requiredOverlap;
        }

        private static bool IsSameTableIdentity(TableSignature original, TableSignature current)
        {
            if (!string.Equals(current.BorderFillId, original.BorderFillId, StringComparison.Ordinal))
            {
                return false;
            }

            return original.FirstLabels.Count == 0 ||
                   string.Equals(original.FirstLabels[0], current.FirstLabels[0], StringComparison.Ordinal);
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
                var grid = HwpxTableModel.BuildGrid(table);
                yield return new TableSignature
                {
                    RowCount = GetInt(table, "rowCnt", table.Elements(Hp + "tr").Count()),
                    ColumnCount = GetInt(table, "colCnt", grid.ColumnCount),
                    Width = size == null ? 0 : GetInt(size, "width", 0),
                    BorderFillId = GetString(table, "borderFillIDRef"),
                    GridOverlapIssues = grid.OverlapIssues.ToList(),
                    FirstLabels = HwpxTableModel.DirectCells(table)
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

        private static string CellText(XElement cell)
        {
            return HwpxTableModel.DirectTextOfCell(cell).Trim();
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

        internal sealed class ValidationOptions
        {
            public ISet<int> AllowedRowGrowthTables { get; private set; }

            public ISet<int> AllowedColumnChangeTables { get; private set; }

            public int MaxLeadingParagraphStyleDrift { get; set; }

            public ValidationOptions()
            {
                AllowedRowGrowthTables = new HashSet<int>();
                AllowedColumnChangeTables = new HashSet<int>();
                MaxLeadingParagraphStyleDrift = 10;
            }
        }

        private sealed class LayoutIssue
        {
            public const string ExpectedChangeSeverity = "expected-change";
            public const string ReviewNeededSeverity = "review-needed";
            public const string BlockingSeverity = "blocking";

            public string Severity { get; private set; }

            public string Message { get; private set; }

            public static LayoutIssue ExpectedChange(string message)
            {
                return new LayoutIssue(ExpectedChangeSeverity, message);
            }

            public static LayoutIssue ReviewNeeded(string message)
            {
                return new LayoutIssue(ReviewNeededSeverity, message);
            }

            public static LayoutIssue Blocking(string message)
            {
                return new LayoutIssue(BlockingSeverity, message);
            }

            private LayoutIssue(string severity, string message)
            {
                Severity = severity;
                Message = message ?? string.Empty;
            }
        }

        private sealed class TableSignature
        {
            public int RowCount { get; set; }

            public int ColumnCount { get; set; }

            public int Width { get; set; }

            public string BorderFillId { get; set; }

            public IList<string> FirstLabels { get; set; }

            public IList<string> GridOverlapIssues { get; set; }
        }

        private sealed class ParagraphSignature
        {
            public string StyleId { get; set; }

            public string ParaPrId { get; set; }

            public string CharPrId { get; set; }
        }
    }
}
