using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace OpenHwp.Automation.Cli
{
    internal static class MarkdownTableParser
    {
        private static readonly Regex MarkdownImagePattern = new Regex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex MarkdownLinkPattern = new Regex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex HtmlBreakPattern = new Regex(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WhitespacePattern = new Regex(@"[ \t]+", RegexOptions.Compiled);

        public static IList<IList<IList<string>>> ParseTables(string markdown)
        {
            var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var tables = new List<IList<IList<string>>>();
            var index = 0;

            while (index < lines.Length)
            {
                var line = lines[index].Trim();
                if (!line.StartsWith("|", StringComparison.Ordinal))
                {
                    index++;
                    continue;
                }

                var rows = new List<IList<string>>();
                while (index < lines.Length && lines[index].Trim().StartsWith("|", StringComparison.Ordinal))
                {
                    var tableLine = lines[index].Trim().Trim('|');
                    if (tableLine.Length > 0 && !IsSeparator(tableLine))
                    {
                        rows.Add(tableLine.Split('|').Select(NormalizeInline).ToList());
                    }

                    index++;
                }

                if (rows.Count > 0)
                {
                    tables.Add(rows);
                }
            }

            return tables;
        }

        private static bool IsSeparator(string tableLine)
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

        private static string NormalizeInline(string text)
        {
            var normalized = HttpUtility.HtmlDecode(text ?? string.Empty);
            normalized = normalized.Replace("&nbsp;", " ");
            normalized = HtmlBreakPattern.Replace(normalized, "\n");
            normalized = MarkdownImagePattern.Replace(normalized, "[image: $1] $2");
            normalized = MarkdownLinkPattern.Replace(normalized, "$1 ($2)");
            normalized = normalized.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
            normalized = Regex.Replace(normalized, @"(?<!\*)\*(?!\*)", string.Empty);
            normalized = Regex.Replace(normalized, @"(?<!_)_(?!_)", string.Empty);
            return WhitespacePattern.Replace(normalized, " ").Trim();
        }
    }
}
