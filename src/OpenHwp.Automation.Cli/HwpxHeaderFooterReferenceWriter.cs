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
    internal static class HwpxHeaderFooterReferenceWriter
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static HeaderFooterReferenceWriteResult Write(
            string inputPath,
            string outputPath,
            string kind,
            string section,
            string idRef,
            string applyPageType,
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

            applyPageType = NormalizeApplyPageType(applyPageType);
            if (occurrenceIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(occurrenceIndex));
            }

            var inputFullPath = Path.GetFullPath(inputPath);
            var outputFullPath = Path.GetFullPath(outputPath);
            var entries = SimpleZipArchive.ReadAll(inputFullPath);
            var result = new HeaderFooterReferenceWriteResult
            {
                InputPath = inputFullPath,
                OutputPath = outputFullPath,
                Kind = kind,
                Section = section ?? string.Empty,
                IdRef = idRef ?? string.Empty,
                ApplyPageType = applyPageType,
                OccurrenceIndex = occurrenceIndex,
                Note = "matching header/footer reference was not found"
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
                    if (!IsHeaderFooterReference(element))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(idRef) &&
                        !string.Equals(GetString(element, "idRef"), idRef, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.MatchedReferences++;
                    if (currentOccurrence != occurrenceIndex)
                    {
                        currentOccurrence++;
                        continue;
                    }

                    result.OldApplyPageType = GetString(element, "applyPageType");
                    element.SetAttributeValue("applyPageType", applyPageType);
                    entries[entry.Key] = SerializeXml(document);
                    SimpleZipArchive.WriteAllPreservingTemplate(inputFullPath, outputFullPath, entries);

                    result.Applied = true;
                    result.PartPath = NormalizePackagePath(entry.Key);
                    result.Section = partSection;
                    result.ResolvedIdRef = GetString(element, "idRef");
                    result.ModifiedReferences = 1;
                    result.Note = "applyPageType updated";
                    WriteReport(result, reportPath);
                    return result;
                }
            }

            WriteReport(result, reportPath);
            return result;
        }

        private static bool IsHeaderFooterReference(XElement element)
        {
            return element != null &&
                   !element.Descendants(Hp + "subList").Any() &&
                   !element.Descendants(Hp + "p").Any() &&
                   !element.Descendants(Hp + "tbl").Any();
        }

        private static string NormalizeApplyPageType(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "BOTH":
                case "EVEN":
                case "ODD":
                    return normalized;
                default:
                    throw new ArgumentException("--apply-page-type must be BOTH, EVEN, or ODD.");
            }
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

        private static void WriteReport(HeaderFooterReferenceWriteResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Header/Footer Reference Write");
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
            AppendReportRow(report, "matched references", result.MatchedReferences.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "modified references", result.ModifiedReferences.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "part", result.PartPath);
            AppendReportRow(report, "idRef filter", result.IdRef);
            AppendReportRow(report, "resolved idRef", result.ResolvedIdRef);
            AppendReportRow(report, "old applyPageType", result.OldApplyPageType);
            AppendReportRow(report, "new applyPageType", result.ApplyPageType);
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

        private static string GetString(XElement element, string name)
        {
            var attribute = element == null ? null : element.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value;
        }
    }

    internal sealed class HeaderFooterReferenceWriteResult
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Kind { get; set; }

        public string Section { get; set; }

        public string IdRef { get; set; }

        public string ResolvedIdRef { get; set; }

        public string ApplyPageType { get; set; }

        public string OldApplyPageType { get; set; }

        public int OccurrenceIndex { get; set; }

        public int MatchedReferences { get; set; }

        public int ModifiedReferences { get; set; }

        public bool Applied { get; set; }

        public string PartPath { get; set; }

        public string Note { get; set; }
    }
}
