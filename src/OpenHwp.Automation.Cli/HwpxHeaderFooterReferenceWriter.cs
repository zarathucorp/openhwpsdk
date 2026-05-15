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

        public static HeaderFooterReferenceAddResult Add(
            string inputPath,
            string outputPath,
            string kind,
            string section,
            string idRef,
            string applyPageType,
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

            idRef = (idRef ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(idRef))
            {
                throw new ArgumentException("--id-ref is required.", nameof(idRef));
            }

            applyPageType = NormalizeApplyPageType(applyPageType);

            var inputFullPath = Path.GetFullPath(inputPath);
            var outputFullPath = Path.GetFullPath(outputPath);
            var entries = SimpleZipArchive.ReadAll(inputFullPath);
            var result = new HeaderFooterReferenceAddResult
            {
                InputPath = inputFullPath,
                OutputPath = outputFullPath,
                Kind = kind,
                Section = section ?? string.Empty,
                IdRef = idRef,
                ApplyPageType = applyPageType,
                Note = "matching reusable header/footer reference was not found"
            };

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

                var elements = document.Root == null
                    ? new List<XElement>()
                    : document.Root.DescendantsAndSelf(Hp + kind).ToList();
                var bodies = elements.Where(IsHeaderFooterBody).ToList();
                var references = elements.Where(IsHeaderFooterReference).ToList();
                var matchingReferences = references
                    .Where(element => string.Equals(GetString(element, "idRef"), idRef, StringComparison.Ordinal))
                    .ToList();
                var applyPageTypeReferences = references
                    .Where(element => string.Equals(GetString(element, "applyPageType"), applyPageType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (bodies.Count == 0 && matchingReferences.Count == 0)
                {
                    continue;
                }

                result.PartPath = NormalizePackagePath(entry.Key);
                result.Section = partSection;
                result.MatchedBodies = bodies.Count;
                result.ExistingReferences = references.Count;
                result.MatchedReferences = matchingReferences.Count;
                result.ApplyPageTypeConflicts = applyPageTypeReferences.Count;

                if (bodies.Count == 0)
                {
                    result.Note = "matching header/footer body was not found";
                    WriteAddReport(result, reportPath);
                    return result;
                }

                if (matchingReferences.Count == 0)
                {
                    result.Note = "matching reusable header/footer reference was not found";
                    WriteAddReport(result, reportPath);
                    return result;
                }

                result.DuplicateReferences = matchingReferences.Count(element =>
                    string.Equals(GetString(element, "applyPageType"), applyPageType, StringComparison.OrdinalIgnoreCase));
                if (applyPageTypeReferences.Count > 0)
                {
                    result.Note = "same kind and applyPageType already exists in section";
                    WriteAddReport(result, reportPath);
                    return result;
                }

                var anchor = matchingReferences.Last();
                var newReference = new XElement(
                    Hp + kind,
                    new XAttribute("idRef", idRef),
                    new XAttribute("applyPageType", applyPageType));
                anchor.AddAfterSelf(newReference);

                entries[entry.Key] = SerializeXml(document);
                SimpleZipArchive.WriteAllPreservingTemplate(inputFullPath, outputFullPath, entries);

                result.Added = true;
                result.AddedReferences = 1;
                result.Note = "header/footer reference added";
                WriteAddReport(result, reportPath);
                return result;
            }

            WriteAddReport(result, reportPath);
            return result;
        }

        private static bool IsHeaderFooterReference(XElement element)
        {
            return element != null &&
                   !element.Descendants(Hp + "subList").Any() &&
                   !element.Descendants(Hp + "p").Any() &&
                   !element.Descendants(Hp + "tbl").Any();
        }

        private static bool IsHeaderFooterBody(XElement element)
        {
            return element != null && !IsHeaderFooterReference(element);
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

        private static void WriteAddReport(HeaderFooterReferenceAddResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Header/Footer Reference Add");
            report.AppendLine();
            report.AppendLine("- input: `" + result.InputPath + "`");
            report.AppendLine("- output: `" + result.OutputPath + "`");
            report.AppendLine("- kind: `" + result.Kind + "`");
            report.AppendLine("- section: `" + result.Section + "`");
            report.AppendLine("- verdict: " + (result.Added ? "added" : "failed"));
            report.AppendLine();
            report.AppendLine("| field | value |");
            report.AppendLine("| --- | --- |");
            AppendReportRow(report, "matched bodies", result.MatchedBodies.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "existing references", result.ExistingReferences.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "matched references", result.MatchedReferences.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "applyPageType conflicts", result.ApplyPageTypeConflicts.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "duplicate references", result.DuplicateReferences.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "added references", result.AddedReferences.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "part", result.PartPath);
            AppendReportRow(report, "idRef", result.IdRef);
            AppendReportRow(report, "applyPageType", result.ApplyPageType);
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

    internal sealed class HeaderFooterReferenceAddResult
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Kind { get; set; }

        public string Section { get; set; }

        public string IdRef { get; set; }

        public string ApplyPageType { get; set; }

        public int MatchedBodies { get; set; }

        public int ExistingReferences { get; set; }

        public int MatchedReferences { get; set; }

        public int ApplyPageTypeConflicts { get; set; }

        public int DuplicateReferences { get; set; }

        public int AddedReferences { get; set; }

        public bool Added { get; set; }

        public string PartPath { get; set; }

        public string Note { get; set; }
    }
}
