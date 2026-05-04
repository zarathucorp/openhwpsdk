using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxContentValidator
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";
        private static readonly Regex MarkdownArtifactPattern = new Regex(@"(!\[|\[[^\]]+\]\([^)]+\)|```|^\s*>|^\s{0,3}#{1,6}\s+|\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?)", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex TodoPlaceholderPattern = new Regex(@"\b(TODO|TBD|FIXME)\b|{{[^}]+}}|<<[^>]+>>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool Validate(string candidatePath, string reportPath, IEnumerable<string> requiredStrings)
        {
            var entries = SimpleZipArchive.ReadAll(Path.GetFullPath(candidatePath));
            var text = ExtractPackageText(entries);
            var issues = new List<string>();
            var warnings = new List<string>();

            foreach (var required in requiredStrings ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(required))
                {
                    continue;
                }

                if (text.IndexOf(required, StringComparison.Ordinal) < 0)
                {
                    issues.Add("FAIL: required text not found: " + required);
                }
            }

            foreach (Match match in MarkdownArtifactPattern.Matches(text))
            {
                issues.Add("FAIL: suspicious Markdown artifact: " + Abbreviate(match.Value.Trim(), 80));
                break;
            }

            foreach (Match match in TodoPlaceholderPattern.Matches(text))
            {
                issues.Add("FAIL: unresolved placeholder token: " + Abbreviate(match.Value.Trim(), 80));
                break;
            }

            foreach (var duplicate in FindRepeatedGuideLines(text))
            {
                warnings.Add("WARN: repeated guide-like text: " + Abbreviate(duplicate, 80));
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Content Validation");
            report.AppendLine();
            report.AppendLine("- candidate: " + Path.GetFullPath(candidatePath));
            report.AppendLine("- extracted text length: " + text.Length.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- required strings: " + (requiredStrings == null ? 0 : requiredStrings.Count()).ToString(CultureInfo.InvariantCulture));
            report.AppendLine();
            report.AppendLine("## Issues");
            if (issues.Count == 0)
            {
                report.AppendLine("- PASS: no content issues detected.");
            }
            else
            {
                foreach (var issue in issues)
                {
                    report.AppendLine("- " + issue);
                }
            }

            if (warnings.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("## Warnings");
                foreach (var warning in warnings)
                {
                    report.AppendLine("- " + warning);
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
            return issues.Count == 0;
        }

        private static string ExtractPackageText(IDictionary<string, byte[]> entries)
        {
            var builder = new StringBuilder();
            foreach (var entry in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!entry.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                XDocument document;
                try
                {
                    document = XDocument.Parse(Encoding.UTF8.GetString(entry.Value), LoadOptions.PreserveWhitespace);
                }
                catch
                {
                    continue;
                }

                foreach (var paragraph in document.Descendants(Hp + "p"))
                {
                    var value = TextOf(paragraph).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        builder.AppendLine(value);
                    }
                }
            }

            return builder.ToString();
        }

        private static IEnumerable<string> FindRepeatedGuideLines(string text)
        {
            return text.Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 12 && (line.StartsWith("※", StringComparison.Ordinal) || line.Contains("작성") || line.Contains("유의")))
                .GroupBy(line => line, StringComparer.Ordinal)
                .Where(group => group.Count() >= 3)
                .Select(group => group.Key);
        }

        private static string TextOf(XElement element)
        {
            var builder = new StringBuilder();
            foreach (var textNode in element.Descendants(Hp + "t"))
            {
                builder.Append(textNode.Value);
            }

            return builder.ToString();
        }

        private static string Abbreviate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }
    }
}
