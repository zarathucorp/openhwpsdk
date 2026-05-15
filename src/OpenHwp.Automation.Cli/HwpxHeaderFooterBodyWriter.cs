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
    internal static class HwpxHeaderFooterBodyWriter
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static HeaderFooterBodyAddResult AddTextBody(
            string inputPath,
            string outputPath,
            string kind,
            string section,
            string idRef,
            string applyPageType,
            string text,
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

            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("--text is required.", nameof(text));
            }

            applyPageType = NormalizeApplyPageType(applyPageType);

            var inputFullPath = Path.GetFullPath(inputPath);
            var outputFullPath = Path.GetFullPath(outputPath);
            var entries = SimpleZipArchive.ReadAll(inputFullPath);
            var result = new HeaderFooterBodyAddResult
            {
                InputPath = inputFullPath,
                OutputPath = outputFullPath,
                Kind = kind,
                Section = section ?? string.Empty,
                IdRef = idRef,
                ApplyPageType = applyPageType,
                Text = text,
                Note = "matching section was not found"
            };

            foreach (var entry in entries.ToList().OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!entry.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var partSection = ExtractSectionName(entry.Key);
                if (!string.Equals(section, partSection, StringComparison.OrdinalIgnoreCase))
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
                    result.Note = "matching section XML could not be parsed";
                    WriteReport(result, reportPath);
                    return result;
                }

                var run = document.Root == null
                    ? null
                    : document.Root.Descendants(Hp + "run").FirstOrDefault();
                if (run == null)
                {
                    result.Note = "matching section has no paragraph run insertion point";
                    WriteReport(result, reportPath);
                    return result;
                }

                var headerFooterElements = document.Root.DescendantsAndSelf()
                    .Where(element => element.Name == Hp + "header" || element.Name == Hp + "footer")
                    .ToList();
                var elements = headerFooterElements
                    .Where(element => element.Name == Hp + kind)
                    .ToList();
                var bodies = elements.Where(IsHeaderFooterBody).ToList();
                var references = elements.Where(IsHeaderFooterReference).ToList();
                var allReferences = headerFooterElements.Where(IsHeaderFooterReference).ToList();
                result.PartPath = NormalizePackagePath(entry.Key);
                result.Section = partSection;
                result.ExistingBodies = bodies.Count;
                result.ExistingReferences = allReferences.Count;
                result.IdRefConflicts = allReferences.Count(element =>
                    string.Equals(GetString(element, "idRef"), idRef, StringComparison.Ordinal));
                result.ApplyPageTypeConflicts = references.Count(element =>
                    string.Equals(GetString(element, "applyPageType"), applyPageType, StringComparison.OrdinalIgnoreCase));

                if (result.IdRefConflicts > 0)
                {
                    result.Note = "same header/footer idRef already exists in section";
                    WriteReport(result, reportPath);
                    return result;
                }

                if (result.ApplyPageTypeConflicts > 0)
                {
                    result.Note = "same kind and applyPageType already exists in section";
                    WriteReport(result, reportPath);
                    return result;
                }

                var body = CreateTextBody(kind, text);
                var reference = new XElement(
                    Hp + kind,
                    new XAttribute("idRef", idRef),
                    new XAttribute("applyPageType", applyPageType));

                var firstReference = run.Elements()
                    .FirstOrDefault(IsAnyHeaderFooterReference);
                if (firstReference != null)
                {
                    firstReference.AddBeforeSelf(body);
                }
                else
                {
                    run.Add(body);
                }

                var lastReference = run.Elements()
                    .Where(IsAnyHeaderFooterReference)
                    .LastOrDefault();
                if (lastReference != null)
                {
                    lastReference.AddAfterSelf(reference);
                }
                else
                {
                    body.AddAfterSelf(reference);
                }

                entries[entry.Key] = SerializeXml(document);
                SimpleZipArchive.WriteAllPreservingTemplate(inputFullPath, outputFullPath, entries);

                result.Added = true;
                result.AddedBodies = 1;
                result.AddedReferences = 1;
                result.Note = "header/footer text body and reference added";
                WriteReport(result, reportPath);
                return result;
            }

            WriteReport(result, reportPath);
            return result;
        }

        private static XElement CreateTextBody(string kind, string text)
        {
            return new XElement(
                Hp + kind,
                new XElement(
                    Hp + "subList",
                    new XElement(
                        Hp + "p",
                        new XElement(
                            Hp + "run",
                            new XElement(Hp + "t", text)))));
        }

        private static bool IsAnyHeaderFooterReference(XElement element)
        {
            return element != null &&
                   (element.Name == Hp + "header" || element.Name == Hp + "footer") &&
                   IsHeaderFooterReference(element);
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

        private static void WriteReport(HeaderFooterBodyAddResult result, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWPX Header/Footer Text Body Add");
            report.AppendLine();
            report.AppendLine("- input: `" + result.InputPath + "`");
            report.AppendLine("- output: `" + result.OutputPath + "`");
            report.AppendLine("- kind: `" + result.Kind + "`");
            report.AppendLine("- section: `" + result.Section + "`");
            report.AppendLine("- verdict: " + (result.Added ? "added" : "failed"));
            report.AppendLine();
            report.AppendLine("| field | value |");
            report.AppendLine("| --- | --- |");
            AppendReportRow(report, "existing bodies", result.ExistingBodies.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "existing references", result.ExistingReferences.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "idRef conflicts", result.IdRefConflicts.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "applyPageType conflicts", result.ApplyPageTypeConflicts.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "added bodies", result.AddedBodies.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "added references", result.AddedReferences.ToString(CultureInfo.InvariantCulture));
            AppendReportRow(report, "part", result.PartPath);
            AppendReportRow(report, "idRef", result.IdRef);
            AppendReportRow(report, "applyPageType", result.ApplyPageType);
            AppendReportRow(report, "text", result.Text);
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

    internal sealed class HeaderFooterBodyAddResult
    {
        public string InputPath { get; set; }

        public string OutputPath { get; set; }

        public string Kind { get; set; }

        public string Section { get; set; }

        public string IdRef { get; set; }

        public string ApplyPageType { get; set; }

        public string Text { get; set; }

        public int ExistingBodies { get; set; }

        public int ExistingReferences { get; set; }

        public int IdRefConflicts { get; set; }

        public int ApplyPageTypeConflicts { get; set; }

        public int AddedBodies { get; set; }

        public int AddedReferences { get; set; }

        public bool Added { get; set; }

        public string PartPath { get; set; }

        public string Note { get; set; }
    }
}
