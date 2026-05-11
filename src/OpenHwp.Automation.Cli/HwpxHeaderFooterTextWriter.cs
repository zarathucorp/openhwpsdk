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
    internal static class HwpxHeaderFooterTextWriter
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static HeaderFooterTextWriteResult Write(
            string inputPath,
            string outputPath,
            string kind,
            string section,
            string anchorText,
            string replacementText,
            int occurrenceIndex,
            string reportPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("Input path is required.", nameof(inputPath));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            }

            kind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind != "header" && kind != "footer")
            {
                throw new ArgumentException("kind must be header or footer.", nameof(kind));
            }

            if (string.IsNullOrEmpty(anchorText))
            {
                throw new ArgumentException("Anchor text is required.", nameof(anchorText));
            }

            if (occurrenceIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(occurrenceIndex));
            }

            var inputFullPath = Path.GetFullPath(inputPath);
            var outputFullPath = Path.GetFullPath(outputPath);
            var entries = SimpleZipArchive.ReadAll(inputFullPath);
            var result = new HeaderFooterTextWriteResult
            {
                InputPath = inputFullPath,
                OutputPath = outputFullPath,
                Kind = kind,
                Section = section ?? string.Empty,
                AnchorText = anchorText,
                ReplacementText = replacementText ?? string.Empty,
                OccurrenceIndex = occurrenceIndex,
                Note = "anchor was not found in matching header/footer body"
            };
            var currentOccurrence = 0;

            foreach (var entry in entries.ToList().OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
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
                catch (XmlException)
                {
                    continue;
                }

                var partSection = ExtractSectionName(entry.Key);
                if (!string.IsNullOrWhiteSpace(section) &&
                    !string.Equals(section, partSection, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var element in document.Root == null
                    ? Enumerable.Empty<XElement>()
                    : document.Root.DescendantsAndSelf(Hp + kind))
                {
                    if (!IsHeaderFooterBody(element))
                    {
                        continue;
                    }

                    result.MatchedBodies++;
                    foreach (var textNode in element.Descendants(Hp + "t"))
                    {
                        var oldText = textNode.Value ?? string.Empty;
                        if (!oldText.Contains(anchorText))
                        {
                            continue;
                        }

                        if (currentOccurrence != occurrenceIndex)
                        {
                            currentOccurrence++;
                            continue;
                        }

                        textNode.Value = oldText.Replace(anchorText, replacementText ?? string.Empty);
                        entries[entry.Key] = SerializeXml(document);
                        SimpleZipArchive.WriteAllPreservingTemplate(inputFullPath, outputFullPath, entries);

                        result.Applied = true;
                        result.PartPath = NormalizePackagePath(entry.Key);
                        result.Section = partSection;
                        result.OldText = oldText;
                        result.NewText = textNode.Value ?? string.Empty;
                        result.ModifiedTextRuns = 1;
                        result.Note = "text run updated";
                        WriteReport(result, reportPath);
                        return result;
                    }
                }
            }

            WriteReport(result, reportPath);
            return result;
        }

        private static bool IsHeaderFooterBody(XElement element)
        {
            return element != null &&
                   (element.Descendants(Hp + "subList").Any() ||
                    element.Descendants(Hp + "p").Any() ||
                    element.Descendants(Hp + "tbl").Any());
        }

        private static string ExtractSectionName(string path)
        {
            var normalized = NormalizePackagePath(path);
            var fileName = Path.GetFileName(normalized);
            if (normalized.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                fileName.StartsWith("section", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(normalized);
            }

            return string.Empty;
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

        private static void WriteReport(HeaderFooterTextWriteResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Header/Footer Text Write");
            report.AppendLine();
            report.AppendLine("- input: `" + result.InputPath + "`");
            report.AppendLine("- output: `" + result.OutputPath + "`");
            report.AppendLine("- kind: `" + result.Kind + "`");
            report.AppendLine("- section: `" + result.Section + "`");
            report.AppendLine("- occurrence: " + result.OccurrenceIndex.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- verdict: " + (result.Applied ? "applied" : "failed"));
            report.AppendLine();
            report.AppendLine("| field | value |");
            report.AppendLine("| --- | --- |");
            AppendReportRow(report, "matched bodies", result.MatchedBodies.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "modified text runs", result.ModifiedTextRuns.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "part", result.PartPath);
            AppendReportRow(report, "anchor", result.AnchorText);
            AppendReportRow(report, "replacement", result.ReplacementText);
            AppendReportRow(report, "old text", result.OldText);
            AppendReportRow(report, "new text", result.NewText);
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
            report.Append(EscapeMarkdownTable(field));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(value));
            report.AppendLine(" |");
        }

        private static string EscapeMarkdownTable(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static string NormalizePackagePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }
    }

    internal sealed class HeaderFooterTextWriteResult
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Kind { get; set; }

        public string Section { get; set; }

        public string AnchorText { get; set; }

        public string ReplacementText { get; set; }

        public int OccurrenceIndex { get; set; }

        public int MatchedBodies { get; set; }

        public int ModifiedTextRuns { get; set; }

        public bool Applied { get; set; }

        public string PartPath { get; set; }

        public string OldText { get; set; }

        public string NewText { get; set; }

        public string Note { get; set; }
    }
}
