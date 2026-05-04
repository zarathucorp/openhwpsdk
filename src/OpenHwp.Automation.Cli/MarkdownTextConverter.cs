using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace OpenHwp.Automation.Cli
{
    internal static class MarkdownTextConverter
    {
        private static readonly Regex HeadingPattern = new Regex(@"^\s{0,3}#{1,6}\s*", RegexOptions.Compiled);
        private static readonly Regex OrderedListPattern = new Regex(@"^\s*\d+\.\s+", RegexOptions.Compiled);
        private static readonly Regex BulletListPattern = new Regex(@"^\s*[-*+]\s+", RegexOptions.Compiled);
        private static readonly Regex BlockQuotePattern = new Regex(@"^\s*>\s?", RegexOptions.Compiled);
        private static readonly Regex MarkdownImagePattern = new Regex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex HorizontalRulePattern = new Regex(@"^\s{0,3}(?:-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Compiled);
        private static readonly Regex TableSeparatorPattern = new Regex(@"^\s*\|?(?:\s*:?-{3,}:?\s*\|)+\s*:?-{3,}:?\s*\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex LinkPattern = new Regex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex HtmlTagPattern = new Regex(@"</?(?:br|p|div|span|strong|em|code|sup|sub)[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MultiBlankLinePattern = new Regex(@"(\r?\n){3,}", RegexOptions.Compiled);

        public static string ToPlainText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var builder = new StringBuilder(markdown.Length);
            var pendingTableLines = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                if (IsTableLine(line))
                {
                    pendingTableLines.Add(line);
                    continue;
                }

                FlushTable(builder, pendingTableLines);
                AppendNormalizedLine(builder, line);
            }

            FlushTable(builder, pendingTableLines);

            var normalized = builder.ToString().Trim();
            normalized = MultiBlankLinePattern.Replace(normalized, Environment.NewLine + Environment.NewLine);
            return normalized;
        }

        public static string ToSubmissionPlainText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return string.Empty;
            }

            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var builder = new StringBuilder(markdown.Length);
            var pendingTableLines = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                if (IsTableLine(line))
                {
                    pendingTableLines.Add(line);
                    continue;
                }

                FlushSubmissionTable(builder, pendingTableLines);
                AppendSubmissionLine(builder, line);
            }

            FlushSubmissionTable(builder, pendingTableLines);

            var normalized = builder.ToString().Trim();
            normalized = MultiBlankLinePattern.Replace(normalized, Environment.NewLine + Environment.NewLine);
            return normalized;
        }

        private static void AppendNormalizedLine(StringBuilder builder, string line)
        {
            var normalized = line.TrimEnd();
            if (normalized.Length == 0)
            {
                builder.AppendLine();
                return;
            }

            normalized = normalized.Replace("<br>", Environment.NewLine)
                .Replace("<br/>", Environment.NewLine)
                .Replace("<br />", Environment.NewLine);
            normalized = HtmlTagPattern.Replace(normalized, string.Empty);
            normalized = LinkPattern.Replace(normalized, "$1 ($2)");
            normalized = HeadingPattern.Replace(normalized, string.Empty);
            normalized = BulletListPattern.Replace(normalized, "- ");
            normalized = OrderedListPattern.Replace(normalized, match => match.Value.Trim() + " ");
            normalized = normalized.Replace("`", string.Empty);
            normalized = normalized.Replace("**", string.Empty);
            normalized = normalized.Replace("__", string.Empty);
            normalized = Regex.Replace(normalized, @"(?<!\*)\*(?!\*)", string.Empty);
            normalized = Regex.Replace(normalized, @"(?<!_)_(?!_)", string.Empty);
            normalized = RemoveUnsupportedSupplementarySymbols(normalized);
            normalized = HttpUtility.HtmlDecode(normalized);

            var parts = normalized.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                builder.AppendLine(part.TrimEnd());
            }
        }

        private static bool IsTableLine(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length > 0 && trimmed.StartsWith("|", StringComparison.Ordinal) && trimmed.Contains("|");
        }

        private static void FlushTable(StringBuilder builder, List<string> tableLines)
        {
            if (tableLines.Count == 0)
            {
                return;
            }

            foreach (var line in tableLines)
            {
                if (TableSeparatorPattern.IsMatch(line))
                {
                    continue;
                }

                var cells = line.Trim().Trim('|').Split('|');
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] = NormalizeInline(cells[i]);
                }

                builder.AppendLine(string.Join(" | ", cells));
            }

            builder.AppendLine();
            tableLines.Clear();
        }

        private static void AppendSubmissionLine(StringBuilder builder, string line)
        {
            var normalized = line.Trim();
            if (normalized.Length == 0 || HorizontalRulePattern.IsMatch(normalized))
            {
                builder.AppendLine();
                return;
            }

            if (BlockQuotePattern.IsMatch(normalized) || MarkdownImagePattern.IsMatch(normalized))
            {
                return;
            }

            normalized = normalized.Replace("<br>", Environment.NewLine)
                .Replace("<br/>", Environment.NewLine)
                .Replace("<br />", Environment.NewLine);
            normalized = HtmlTagPattern.Replace(normalized, string.Empty);
            normalized = LinkPattern.Replace(normalized, "$1 ($2)");
            normalized = HeadingPattern.Replace(normalized, string.Empty);
            normalized = BulletListPattern.Replace(normalized, "- ");
            normalized = OrderedListPattern.Replace(normalized, match => match.Value.Trim() + " ");
            normalized = normalized.Replace("❖", string.Empty);
            normalized = NormalizeInline(normalized);
            normalized = Regex.Replace(normalized, @"^F\s+", string.Empty);

            if (normalized.Length == 0)
            {
                return;
            }

            foreach (var part in normalized.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    builder.AppendLine(trimmed);
                }
            }
        }

        private static void FlushSubmissionTable(StringBuilder builder, List<string> tableLines)
        {
            if (tableLines.Count == 0)
            {
                return;
            }

            foreach (var line in tableLines)
            {
                if (TableSeparatorPattern.IsMatch(line))
                {
                    continue;
                }

                var cells = line.Trim().Trim('|').Split('|');
                for (var index = 0; index < cells.Length; index++)
                {
                    cells[index] = NormalizeInline(cells[index]);
                }

                var joined = string.Join(" / ", cells);
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    builder.AppendLine(joined);
                }
            }

            builder.AppendLine();
            tableLines.Clear();
        }

        private static string NormalizeInline(string text)
        {
            var normalized = (text ?? string.Empty).Trim();
            normalized = normalized.Replace("<br>", " / ")
                .Replace("<br/>", " / ")
                .Replace("<br />", " / ");
            normalized = LinkPattern.Replace(normalized, "$1 ($2)");
            normalized = normalized.Replace("`", string.Empty);
            normalized = normalized.Replace("**", string.Empty);
            normalized = normalized.Replace("__", string.Empty);
            normalized = Regex.Replace(normalized, @"(?<!\*)\*(?!\*)", string.Empty);
            normalized = Regex.Replace(normalized, @"(?<!_)_(?!_)", string.Empty);
            normalized = RemoveUnsupportedSupplementarySymbols(normalized);
            normalized = HttpUtility.HtmlDecode(normalized);
            return normalized.Trim();
        }

        private static string RemoveUnsupportedSupplementarySymbols(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return Regex.Replace(value, @"[\uD800-\uDBFF][\uDC00-\uDFFF]", match =>
            {
                var codePoint = char.ConvertToUtf32(match.Value, 0);
                return codePoint >= 0xF0000 && codePoint <= 0xFFFFD ? match.Value : string.Empty;
            });
        }
    }
}
