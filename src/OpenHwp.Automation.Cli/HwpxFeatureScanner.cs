using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxFeatureScanner
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static ScanSummary Scan(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("Input path is required.", nameof(inputPath));
            }

            var fullPath = Path.GetFullPath(inputPath);
            var files = ResolveHwpxFiles(fullPath);
            var summary = new ScanSummary(fullPath);
            foreach (var file in files)
            {
                var result = ScanFile(file);
                summary.Files.Add(result);
                summary.Add(result);
            }

            return summary;
        }

        public static void WriteMarkdown(ScanSummary summary, string reportPath)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("# HWPX Feature Scan");
            builder.AppendLine();
            builder.AppendLine("- input: " + summary.InputPath);
            builder.AppendLine("- files: " + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- package errors: " + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- XML parse errors: " + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();

            builder.AppendLine("## Aggregate Counts");
            builder.AppendLine();
            builder.AppendLine("| feature | count |");
            builder.AppendLine("| --- | ---: |");
            foreach (var item in summary.Aggregate.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                builder.AppendLine("| " + item.Key + " | " + item.Value.ToString(CultureInfo.InvariantCulture) + " |");
            }

            builder.AppendLine();
            builder.AppendLine("## Authoring Coverage");
            builder.AppendLine();
            builder.AppendLine("| area | observed | current support | gap |");
            builder.AppendLine("| --- | ---: | --- | --- |");
            AppendCoverageRow(builder, "paragraph text", summary.Count("paragraphs"), "read/write via COM and package anchors", "generic Markdown structure rendering remains limited");
            AppendCoverageRow(builder, "character/paragraph styles", summary.Count("charStyles") + summary.Count("paragraphStyles"), "preserve existing styles; guard tiny charPr on writes", "style creation/editing is not general-purpose");
            AppendCoverageRow(builder, "tables", summary.Count("tables"), "form-map extraction, package cell writes, merge/nested-table aware model", "arbitrary table authoring is template-driven");
            AppendCoverageRow(builder, "merged cells", summary.Count("mergedCells"), "detected and guarded; forced writes require explicit opt-in", "no generic merged-cell creation API");
            AppendCoverageRow(builder, "nested tables", summary.Count("nestedTables"), "detected; parent-cell text separated from nested content", "no generic nested-table creation API");
            AppendCoverageRow(builder, "images", summary.Count("pictures") + summary.Count("embeddedImages"), "COM insertion and package-level BinData/hp:pic embedding", "editor-specific wrapping/positioning needs visual verification");
            AppendCoverageRow(builder, "drawing shapes", summary.Count("drawingShapes"), "inventory only with type-level signal counts", "line/rect/ellipse/textbox/group editing is not implemented");
            AppendCoverageRow(builder, "fields/forms", summary.Count("fields") + summary.Count("fieldBegins") + summary.Count("pressFields") + summary.Count("formObjects"), "COM field commands exist; marker counts are reported separately", "package-level field/form extraction and writing are not implemented");
            AppendCoverageRow(builder, "headers/footers", summary.Count("pageHeaders") + summary.Count("pageFooters") + summary.Count("pageHeaderReferences") + summary.Count("pageFooterReferences"), "inventory only", "header/footer writing and section-aware editing are not implemented");
            AppendCoverageRow(builder, "footnotes/endnotes", summary.Count("footnotes") + summary.Count("endnotes"), "inventory only", "footnote/endnote insertion and editing are not implemented");
            AppendCoverageRow(builder, "equations/charts/OLE", summary.Count("equations") + summary.Count("charts") + summary.Count("oleObjects"), "inventory only", "equation/chart/OLE insertion and editing are not implemented");
            AppendCoverageRow(builder, "captions/bookmarks/references", CountReferenceSignals(summary), "inventory only", "caption/reference insertion and refresh are not implemented");
            AppendCoverageRow(builder, "media", summary.Count("videos") + summary.Count("sounds"), "inventory only", "media insertion/editing is not implemented");

            builder.AppendLine();
            builder.AppendLine("## Detailed Feature Groups");
            builder.AppendLine();
            AppendFeatureGroup(builder, summary, "Package Parts", new[] { "entries", "sectionParts", "documentHeadParts", "pageHeaderParts", "pageFooterParts", "footnoteParts", "endnoteParts", "binDataEntries", "imageBinDataEntries", "oleLikeBinDataEntries", "mediaBinDataEntries" });
            AppendFeatureGroup(builder, summary, "Headers And Notes", new[] { "pageHeaders", "pageFooters", "pageHeaderReferences", "pageFooterReferences", "footnotes", "endnotes", "memos", "comments" });
            AppendFeatureGroup(builder, summary, "Fields And Forms", new[] { "fieldMarkers", "fields", "fieldBegins", "fieldEnds", "pressFields", "formObjects", "checkBoxes", "radioButtons", "comboBoxes", "editFields" });
            AppendFeatureGroup(builder, summary, "Shapes", new[] { "drawingShapes", "lines", "rectangles", "ellipses", "arcs", "polygons", "curves", "containers", "groups", "textBoxes", "textArt", "shapeObjects" });
            AppendFeatureGroup(builder, summary, "References", new[] { "captions", "bookmarks", "crossReferences", "hyperlinks", "tocMarkers", "indexMarkers", "autoNumbers", "pageNumbers" });
            AppendFeatureGroup(builder, summary, "Embedded Objects", new[] { "equations", "charts", "oleObjects", "videos", "sounds" });
            AppendHeaderFooterInventory(builder, summary);
            AppendFieldFormInventory(builder, summary);
            AppendReferenceInventory(builder, summary);

            builder.AppendLine();
            builder.AppendLine("## Missing Corpus Signals");
            builder.AppendLine();
            foreach (var item in GetMissingCorpusSignals(summary))
            {
                builder.AppendLine("- " + item);
            }
            builder.AppendLine();
            builder.AppendLine("Counts in this scan are inventory signals, not proof that writing/editing is implemented. `oleLikeBinDataEntries` is a filename/extension heuristic and should be confirmed with a fixture before treating it as a real OLE object.");

            builder.AppendLine();
            builder.AppendLine("## Files");
            builder.AppendLine();
            builder.AppendLine("| file | tables | cells | merged | nested | pictures | bindata | shapes | fields/forms | headers | notes | refs | objects | xml errors |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            foreach (var file in summary.Files.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append("| ");
                builder.Append(EscapeMarkdown(Path.GetFileName(file.Path)));
                builder.Append(" | ");
                builder.Append(file.Count("tables").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("tableCells").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("mergedCells").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("nestedTables").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("pictures").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("binDataEntries").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.Count("drawingShapes").ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append((file.Count("fields") + file.Count("fieldBegins") + file.Count("pressFields") + file.Count("formObjects")).ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append((file.Count("pageHeaders") + file.Count("pageFooters") + file.Count("pageHeaderReferences") + file.Count("pageFooterReferences")).ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append((file.Count("footnotes") + file.Count("endnotes") + file.Count("memos") + file.Count("comments")).ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(CountReferenceSignals(file).ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append((file.Count("equations") + file.Count("charts") + file.Count("oleObjects") + file.Count("videos") + file.Count("sounds")).ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(file.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(" |");
            }

            var errors = summary.Files.Where(item => item.PackageError.Length > 0 || item.XmlErrors.Count > 0).ToList();
            if (errors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("## Errors");
                foreach (var file in errors)
                {
                    if (file.PackageError.Length > 0)
                    {
                        builder.AppendLine("- " + file.Path + ": " + file.PackageError);
                    }

                    foreach (var error in file.XmlErrors)
                    {
                        builder.AppendLine("- " + file.Path + " / " + error);
                    }
                }
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, builder.ToString(), new UTF8Encoding(true));
        }

        private static void AppendCoverageRow(StringBuilder builder, string area, int observed, string support, string gap)
        {
            builder.Append("| ");
            builder.Append(area);
            builder.Append(" | ");
            builder.Append(observed.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(support);
            builder.Append(" | ");
            builder.Append(gap);
            builder.AppendLine(" |");
        }

        private static int CountReferenceSignals(ScanSummary summary)
        {
            return summary.Count("captions") +
                   summary.Count("bookmarks") +
                   summary.Count("crossReferences") +
                   summary.Count("hyperlinks") +
                   summary.Count("tocMarkers") +
                   summary.Count("indexMarkers") +
                   summary.Count("autoNumbers") +
                   summary.Count("pageNumbers");
        }

        private static int CountReferenceSignals(FileScanResult file)
        {
            return file.Count("captions") +
                   file.Count("bookmarks") +
                   file.Count("crossReferences") +
                   file.Count("hyperlinks") +
                   file.Count("tocMarkers") +
                   file.Count("indexMarkers") +
                   file.Count("autoNumbers") +
                   file.Count("pageNumbers");
        }

        private static void AppendFeatureGroup(StringBuilder builder, ScanSummary summary, string title, IEnumerable<string> featureNames)
        {
            builder.AppendLine("### " + title);
            builder.AppendLine();
            builder.AppendLine("| feature | count |");
            builder.AppendLine("| --- | ---: |");
            foreach (var name in featureNames)
            {
                builder.Append("| ");
                builder.Append(name);
                builder.Append(" | ");
                builder.Append(summary.Count(name).ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(" |");
            }

            builder.AppendLine();
        }

        private static IEnumerable<string> GetMissingCorpusSignals(ScanSummary summary)
        {
            var missing = new List<string>();
            AddMissingSignal(missing, summary, "page header body", "pageHeaders", "pageHeaderParts");
            AddMissingSignal(missing, summary, "page footer body", "pageFooters", "pageFooterParts");
            AddMissingSignal(missing, summary, "page header reference", "pageHeaderReferences");
            AddMissingSignal(missing, summary, "page footer reference", "pageFooterReferences");
            AddMissingSignal(missing, summary, "footnote body", "footnotes", "footnoteParts");
            AddMissingSignal(missing, summary, "endnote body", "endnotes", "endnoteParts");
            AddMissingSignal(missing, summary, "memo", "memos");
            AddMissingSignal(missing, summary, "comment", "comments");
            AddMissingSignal(missing, summary, "press field / 누름틀", "pressFields");
            AddMissingSignal(missing, summary, "form object", "formObjects");
            AddMissingSignal(missing, summary, "bookmark", "bookmarks");
            AddMissingSignal(missing, summary, "caption", "captions");
            AddMissingSignal(missing, summary, "cross reference", "crossReferences");
            AddMissingSignal(missing, summary, "table of contents marker", "tocMarkers");
            AddMissingSignal(missing, summary, "index marker", "indexMarkers");
            AddMissingSignal(missing, summary, "text box", "textBoxes");
            AddMissingSignal(missing, summary, "equation", "equations");
            AddMissingSignal(missing, summary, "chart", "charts");
            AddMissingSignal(missing, summary, "OLE object", "oleObjects", "oleLikeBinDataEntries");
            AddMissingSignal(missing, summary, "video", "videos");
            AddMissingSignal(missing, summary, "sound", "sounds");
            return missing.Count == 0 ? new[] { "No tracked feature signal is missing from the current corpus." } : missing;
        }

        private static void AddMissingSignal(ICollection<string> missing, ScanSummary summary, string label, params string[] featureNames)
        {
            if (featureNames.All(name => summary.Count(name) == 0))
            {
                missing.Add(label + " fixture is missing or not detected");
            }
        }

        private static void AppendHeaderFooterInventory(StringBuilder builder, ScanSummary summary)
        {
            var rows = summary.Files
                .SelectMany(file => file.HeaderFooterItems.Select(detail => new { File = file, Detail = detail }))
                .ToList();
            if (rows.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Header/Footer Inventory");
            builder.AppendLine();
            builder.AppendLine("| file | part | kind | role | ref | paragraphs | tables | pictures | shapes | text |");
            builder.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- |");
            foreach (var row in rows.OrderBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Detail.PartPath, StringComparer.Ordinal).ThenBy(item => item.Detail.Kind, StringComparer.Ordinal))
            {
                builder.Append("| ");
                builder.Append(EscapeMarkdown(Path.GetFileName(row.File.Path)));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.PartPath));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.Kind));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.Role));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.Reference));
                builder.Append(" | ");
                builder.Append(row.Detail.Paragraphs.ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(row.Detail.Tables.ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(row.Detail.Pictures.ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(row.Detail.Shapes.ToString(CultureInfo.InvariantCulture));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(Truncate(row.Detail.Text, 80)));
                builder.AppendLine(" |");
            }
        }

        private static void AppendFieldFormInventory(StringBuilder builder, ScanSummary summary)
        {
            var rows = summary.Files
                .SelectMany(file => file.FieldFormItems.Select(detail => new { File = file, Detail = detail }))
                .ToList();
            if (rows.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Field/Form Inventory");
            builder.AppendLine();
            builder.AppendLine("| file | part | kind | attrs | text |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var row in rows.OrderBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Detail.PartPath, StringComparer.Ordinal).ThenBy(item => item.Detail.Kind, StringComparer.Ordinal).ThenBy(item => item.Detail.Attributes, StringComparer.Ordinal))
            {
                builder.Append("| ");
                builder.Append(EscapeMarkdown(Path.GetFileName(row.File.Path)));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.PartPath));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.Kind));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.Attributes));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(Truncate(row.Detail.Text, 80)));
                builder.AppendLine(" |");
            }
        }

        private static void AppendReferenceInventory(StringBuilder builder, ScanSummary summary)
        {
            var rows = summary.Files
                .SelectMany(file => file.ReferenceItems.Select(detail => new { File = file, Detail = detail }))
                .ToList();
            if (rows.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Reference Inventory");
            builder.AppendLine();
            builder.AppendLine("| file | part | kind | attrs | text |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var row in rows.OrderBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Detail.PartPath, StringComparer.Ordinal).ThenBy(item => item.Detail.Kind, StringComparer.Ordinal).ThenBy(item => item.Detail.Attributes, StringComparer.Ordinal))
            {
                builder.Append("| ");
                builder.Append(EscapeMarkdown(Path.GetFileName(row.File.Path)));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.PartPath));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.Kind));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(row.Detail.Attributes));
                builder.Append(" | ");
                builder.Append(EscapeMarkdown(Truncate(row.Detail.Text, 80)));
                builder.AppendLine(" |");
            }
        }

        private static IList<string> ResolveHwpxFiles(string fullPath)
        {
            if (File.Exists(fullPath))
            {
                if (!fullPath.EndsWith(".hwpx", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Input file must be .hwpx: " + fullPath);
                }

                return new List<string> { fullPath };
            }

            if (!Directory.Exists(fullPath))
            {
                throw new FileNotFoundException("Input path was not found.", fullPath);
            }

            return Directory.GetFiles(fullPath, "*.hwpx", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static FileScanResult ScanFile(string path)
        {
            var result = new FileScanResult(path);
            IDictionary<string, byte[]> entries;
            try
            {
                entries = SimpleZipArchive.ReadAll(path);
            }
            catch (Exception ex)
            {
                result.PackageError = ex.GetType().Name + ": " + ex.Message;
                return result;
            }

            result.Add("entries", entries.Count);
            result.Add("binDataEntries", entries.Keys.Count(name => name.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase)));
            result.Add("imageBinDataEntries", entries.Keys.Count(IsImageBinDataEntry));
            result.Add("oleLikeBinDataEntries", entries.Keys.Count(IsOleLikeBinDataEntry));
            result.Add("mediaBinDataEntries", entries.Keys.Count(IsMediaBinDataEntry));
            result.Add("sectionParts", entries.Keys.Count(name => name.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
            result.Add("documentHeadParts", entries.Keys.Count(IsDocumentHeadPart));
            result.Add("pageHeaderParts", entries.Keys.Count(IsPageHeaderPart));
            result.Add("pageFooterParts", entries.Keys.Count(IsPageFooterPart));
            result.Add("footnoteParts", entries.Keys.Count(IsFootnotePart));
            result.Add("endnoteParts", entries.Keys.Count(IsEndnotePart));

            foreach (var entry in entries.Where(item => IsXmlEntry(item.Key)))
            {
                XDocument document;
                try
                {
                    document = XDocument.Parse(Encoding.UTF8.GetString(entry.Value), LoadOptions.PreserveWhitespace);
                }
                catch (Exception ex)
                {
                    result.XmlParseErrors++;
                    result.XmlErrors.Add(entry.Key + ": " + ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

                ScanDocument(entry.Key, document, result);
            }

            return result;
        }

        private static void ScanDocument(string path, XDocument document, FileScanResult result)
        {
            var tables = document.Descendants(Hp + "tbl").ToList();
            result.Add("tables", tables.Count);
            result.Add("tableCells", document.Descendants(Hp + "tc").Count());
            result.Add("paragraphs", document.Descendants(Hp + "p").Count());
            result.Add("textRuns", document.Descendants(Hp + "t").Count());
            result.Add("mergedCells", document.Descendants(Hp + "cellSpan").Count(span => GetInt(span, "rowSpan", 1) > 1 || GetInt(span, "colSpan", 1) > 1));
            result.Add("nestedTables", document.Descendants(Hp + "tc").Count(cell => cell.Descendants(Hp + "tbl").Any()));

            foreach (var element in document.Descendants())
            {
                switch (element.Name.LocalName)
                {
                    case "autoNum":
                    case "autoNumFormat":
                        AddReferenceDetail(path, element, result, "autoNumber", "autoNumbers");
                        break;
                    case "bookmark":
                    case "bookMark":
                    case "bookmarkBegin":
                    case "bookMarkBegin":
                        AddReferenceDetail(path, element, result, "bookmark", "bookmarks");
                        break;
                    case "caption":
                    case "cap":
                        AddReferenceDetail(path, element, result, "caption", "captions");
                        break;
                    case "charPr":
                        result.Increment("charStyles");
                        break;
                    case "checkBtn":
                    case "checkBox":
                        AddFieldFormDetail(path, element, result, "checkBox", "checkBoxes", "formObjects");
                        break;
                    case "comboBox":
                    case "comboBtn":
                    case "listBox":
                        AddFieldFormDetail(path, element, result, "comboBox", "comboBoxes", "formObjects");
                        break;
                    case "comment":
                        if (IsAuthoringContentPart(path) || IsHwpParagraphElement(element))
                        {
                            result.Increment("comments");
                        }
                        break;
                    case "paraPr":
                        result.Increment("paragraphStyles");
                        break;
                    case "pic":
                        result.Increment("pictures");
                        break;
                    case "img":
                        result.Increment("embeddedImages");
                        break;
                    case "ctrl":
                        result.Increment("controls");
                        break;
                    case "fieldBegin":
                        AddFieldFormDetail(path, element, result, "fieldBegin", "fieldBegins", "fieldMarkers");
                        break;
                    case "fieldEnd":
                        IncrementAuthoringFieldFormSignal(path, element, result, "fieldEnds", "fieldMarkers");
                        break;
                    case "field":
                        AddFieldFormDetail(path, element, result, "field", "fields", "fieldMarkers");
                        break;
                    case "formObject":
                        AddFieldFormDetail(path, element, result, "formObject", "formObjects");
                        break;
                    case "header":
                        if (IsHwpParagraphElement(element))
                        {
                            IncrementHeaderOrFooter(path, element, result, "pageHeaders", "pageHeaderReferences");
                        }
                        break;
                    case "footer":
                        if (IsHwpParagraphElement(element))
                        {
                            IncrementHeaderOrFooter(path, element, result, "pageFooters", "pageFooterReferences");
                        }
                        break;
                    case "footNote":
                        result.Increment("footnotes");
                        break;
                    case "endNote":
                        result.Increment("endnotes");
                        break;
                    case "hyperlink":
                    case "hyplnk":
                        AddReferenceDetail(path, element, result, "hyperlink", "hyperlinks");
                        break;
                    case "idxmark":
                    case "indexmark":
                        AddReferenceDetail(path, element, result, "indexMarker", "indexMarkers");
                        break;
                    case "memo":
                        result.Increment("memos");
                        break;
                    case "equation":
                        result.Increment("equations");
                        break;
                    case "chart":
                        result.Increment("charts");
                        break;
                    case "ole":
                    case "oleObject":
                        result.Increment("oleObjects");
                        break;
                    case "pageNum":
                    case "pageNumCtrl":
                    case "pageNumFormat":
                        AddReferenceDetail(path, element, result, "pageNumber", "pageNumbers");
                        break;
                    case "radioBtn":
                    case "radioButton":
                        AddFieldFormDetail(path, element, result, "radioButton", "radioButtons", "formObjects");
                        break;
                    case "crossRef":
                    case "crossReference":
                        AddReferenceDetail(path, element, result, "crossReference", "crossReferences");
                        break;
                    case "scrollBar":
                    case "edit":
                    case "editField":
                        AddFieldFormDetail(path, element, result, "editField", "editFields", "formObjects");
                        break;
                    case "snd":
                    case "sound":
                        result.Increment("sounds");
                        break;
                    case "textBox":
                        IncrementDrawingShape(path, element, result, "textBoxes");
                        break;
                    case "toc":
                    case "tocMark":
                    case "tocmark":
                    case "tableOfContents":
                        AddReferenceDetail(path, element, result, "tocMarker", "tocMarkers");
                        break;
                    case "press":
                    case "pressField":
                    case "placeholder":
                        AddFieldFormDetail(path, element, result, "pressField", "pressFields", "fieldMarkers");
                        break;
                    case "video":
                        result.Increment("videos");
                        break;
                    case "line":
                        IncrementDrawingShape(path, element, result, "lines");
                        break;
                    case "rect":
                        IncrementDrawingShape(path, element, result, "rectangles");
                        break;
                    case "ellipse":
                        IncrementDrawingShape(path, element, result, "ellipses");
                        break;
                    case "arc":
                        IncrementDrawingShape(path, element, result, "arcs");
                        break;
                    case "polygon":
                        IncrementDrawingShape(path, element, result, "polygons");
                        break;
                    case "curve":
                        IncrementDrawingShape(path, element, result, "curves");
                        break;
                    case "container":
                        IncrementDrawingShape(path, element, result, "containers");
                        break;
                    case "group":
                    case "grp":
                        IncrementDrawingShape(path, element, result, "groups");
                        break;
                    case "textart":
                        IncrementDrawingShape(path, element, result, "textArt");
                        break;
                    case "shapeObject":
                        IncrementDrawingShape(path, element, result, "shapeObjects");
                        break;
                }
            }
        }

        private static void IncrementDrawingShape(string path, XElement element, FileScanResult result, string featureName)
        {
            if (!IsAuthoringContentPart(path) || !IsHwpParagraphElement(element))
            {
                return;
            }

            result.Increment(featureName);
            result.Increment("drawingShapes");
        }

        private static void AddFieldFormDetail(string path, XElement element, FileScanResult result, string kind, params string[] countNames)
        {
            if (!IncrementAuthoringFieldFormSignal(path, element, result, countNames))
            {
                return;
            }

            result.FieldFormItems.Add(new FieldFormDetail(
                NormalizePackagePath(path),
                kind,
                ExtractKnownAttributes(element, new[] { "name", "id", "idRef", "type", "command", "fieldName", "value" }),
                string.Join(" ", element.Descendants(Hp + "t").Select(item => item.Value).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray())));
        }

        private static bool IncrementAuthoringFieldFormSignal(string path, XElement element, FileScanResult result, params string[] countNames)
        {
            if (!IsAuthoringContentPart(path) || !IsHwpParagraphElement(element))
            {
                return false;
            }

            foreach (var name in countNames)
            {
                result.Increment(name);
            }

            return true;
        }

        private static void AddReferenceDetail(string path, XElement element, FileScanResult result, string kind, params string[] countNames)
        {
            if (!IncrementAuthoringReferenceSignal(path, element, result, countNames))
            {
                return;
            }

            result.ReferenceItems.Add(new ReferenceDetail(
                NormalizePackagePath(path),
                kind,
                ExtractKnownAttributes(element, new[] { "name", "id", "idRef", "target", "targetId", "number", "numType", "text" }),
                string.Join(" ", element.Descendants(Hp + "t").Select(item => item.Value).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray())));
        }

        private static bool IncrementAuthoringReferenceSignal(string path, XElement element, FileScanResult result, params string[] countNames)
        {
            if (!IsAuthoringContentPart(path) || !IsHwpParagraphElement(element))
            {
                return false;
            }

            foreach (var name in countNames)
            {
                result.Increment(name);
            }

            return true;
        }

        private static void IncrementHeaderOrFooter(string path, XElement element, FileScanResult result, string bodyCountName, string referenceCountName)
        {
            if (IsHeaderOrFooterBody(path, element, bodyCountName))
            {
                result.Increment(bodyCountName);
                result.HeaderFooterItems.Add(CreateHeaderFooterDetail(path, element, bodyCountName == "pageHeaders" ? "header" : "footer", "body"));
            }
            else
            {
                result.Increment(referenceCountName);
                result.HeaderFooterItems.Add(CreateHeaderFooterDetail(path, element, bodyCountName == "pageHeaders" ? "header" : "footer", "reference"));
            }
        }

        private static HeaderFooterDetail CreateHeaderFooterDetail(string path, XElement element, string kind, string role)
        {
            return new HeaderFooterDetail(
                NormalizePackagePath(path),
                kind,
                role,
                element.Descendants(Hp + "p").Count(),
                element.Descendants(Hp + "tbl").Count(),
                element.Descendants(Hp + "pic").Count(),
                CountDrawingShapeElements(path, element),
                ExtractHeaderFooterReference(element),
                string.Join(" ", element.Descendants(Hp + "t").Select(item => item.Value).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()));
        }

        private static string ExtractHeaderFooterReference(XElement element)
        {
            return ExtractKnownAttributes(element, new[] { "idRef", "id", "name", "applyPageType", "textFlow" });
        }

        private static string ExtractKnownAttributes(XElement element, IEnumerable<string> names)
        {
            var parts = new List<string>();
            foreach (var name in names)
            {
                var attribute = element.Attribute(name);
                if (attribute != null && !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    parts.Add(name + "=" + attribute.Value);
                }
            }

            return string.Join("; ", parts.ToArray());
        }

        private static int CountDrawingShapeElements(string path, XElement element)
        {
            if (!IsAuthoringContentPart(path))
            {
                return 0;
            }

            return element.Descendants().Count(item => IsHwpParagraphElement(item) && IsDrawingShapeElement(item));
        }

        private static bool IsHeaderOrFooterBody(string path, XElement element, string bodyCountName)
        {
            if ((bodyCountName == "pageHeaders" && IsPageHeaderPart(path)) ||
                (bodyCountName == "pageFooters" && IsPageFooterPart(path)))
            {
                return true;
            }

            return IsSectionPart(path) &&
                   IsHwpParagraphElement(element) &&
                   (element.Descendants(Hp + "subList").Any() ||
                    element.Descendants(Hp + "p").Any() ||
                    element.Descendants(Hp + "tbl").Any());
        }

        private static bool IsXmlEntry(string path)
        {
            return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".rdf", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAuthoringContentPart(string path)
        {
            return IsSectionPart(path) ||
                   IsPageHeaderPart(path) ||
                   IsPageFooterPart(path) ||
                   IsFootnotePart(path) ||
                   IsEndnotePart(path);
        }

        private static bool IsSectionPart(string path)
        {
            var normalized = NormalizePackagePath(path);
            var fileName = Path.GetFileName(normalized);
            return normalized.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                   fileName.StartsWith("section", StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDocumentHeadPart(string path)
        {
            return string.Equals(NormalizePackagePath(path), "Contents/header.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPageHeaderPart(string path)
        {
            var normalized = NormalizePackagePath(path);
            return normalized.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                   normalized.IndexOf("header", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   !IsDocumentHeadPart(normalized);
        }

        private static bool IsPageFooterPart(string path)
        {
            var normalized = NormalizePackagePath(path);
            return normalized.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                   normalized.IndexOf("footer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFootnotePart(string path)
        {
            var normalized = NormalizePackagePath(path);
            return normalized.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                   normalized.IndexOf("footnote", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsEndnotePart(string path)
        {
            var normalized = NormalizePackagePath(path);
            return normalized.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                   normalized.IndexOf("endnote", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHwpParagraphElement(XElement element)
        {
            return element.Name.NamespaceName == Hp.NamespaceName;
        }

        private static bool IsDrawingShapeElement(XElement element)
        {
            switch (element.Name.LocalName)
            {
                case "line":
                case "rect":
                case "ellipse":
                case "arc":
                case "polygon":
                case "curve":
                case "container":
                case "group":
                case "grp":
                case "textBox":
                case "textart":
                case "shapeObject":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsImageBinDataEntry(string path)
        {
            var normalized = NormalizePackagePath(path);
            return normalized.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase) &&
                   HasExtension(normalized, ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff");
        }

        private static bool IsOleLikeBinDataEntry(string path)
        {
            var normalized = NormalizePackagePath(path);
            return normalized.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase) &&
                   (HasExtension(normalized, ".ole", ".bin", ".dat") ||
                    normalized.IndexOf("ole", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsMediaBinDataEntry(string path)
        {
            var normalized = NormalizePackagePath(path);
            return normalized.StartsWith("BinData/", StringComparison.OrdinalIgnoreCase) &&
                   HasExtension(normalized, ".mp4", ".avi", ".wmv", ".wav", ".mp3", ".m4a");
        }

        private static bool HasExtension(string path, params string[] extensions)
        {
            var extension = Path.GetExtension(path ?? string.Empty);
            return extensions.Any(item => string.Equals(extension, item, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizePackagePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }

        private static int GetInt(XElement element, string attributeName, int defaultValue)
        {
            var attribute = element.Attribute(attributeName);
            int value;
            return attribute != null && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
        }

        private static string EscapeMarkdown(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        internal sealed class ScanSummary
        {
            public ScanSummary(string inputPath)
            {
                InputPath = inputPath;
                Files = new List<FileScanResult>();
                Aggregate = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            public string InputPath { get; private set; }

            public IList<FileScanResult> Files { get; private set; }

            public IDictionary<string, int> Aggregate { get; private set; }

            public int PackageErrors { get; private set; }

            public int XmlParseErrors { get; private set; }

            public int Count(string name)
            {
                int value;
                return Aggregate.TryGetValue(name, out value) ? value : 0;
            }

            public void Add(FileScanResult result)
            {
                if (result == null)
                {
                    return;
                }

                if (result.PackageError.Length > 0)
                {
                    PackageErrors++;
                }

                XmlParseErrors += result.XmlParseErrors;
                foreach (var item in result.Counts)
                {
                    Aggregate[item.Key] = Count(item.Key) + item.Value;
                }
            }
        }

        internal sealed class FileScanResult
        {
            public FileScanResult(string path)
            {
                Path = path;
                Counts = new Dictionary<string, int>(StringComparer.Ordinal);
                XmlErrors = new List<string>();
                HeaderFooterItems = new List<HeaderFooterDetail>();
                FieldFormItems = new List<FieldFormDetail>();
                ReferenceItems = new List<ReferenceDetail>();
                PackageError = string.Empty;
            }

            public string Path { get; private set; }

            public IDictionary<string, int> Counts { get; private set; }

            public int XmlParseErrors { get; set; }

            public IList<string> XmlErrors { get; private set; }

            public IList<HeaderFooterDetail> HeaderFooterItems { get; private set; }

            public IList<FieldFormDetail> FieldFormItems { get; private set; }

            public IList<ReferenceDetail> ReferenceItems { get; private set; }

            public string PackageError { get; set; }

            public int Count(string name)
            {
                int value;
                return Counts.TryGetValue(name, out value) ? value : 0;
            }

            public void Increment(string name)
            {
                Add(name, 1);
            }

            public void Add(string name, int value)
            {
                if (value == 0)
                {
                    return;
                }

                Counts[name] = Count(name) + value;
            }
        }

        internal sealed class HeaderFooterDetail
        {
            public HeaderFooterDetail(string partPath, string kind, string role, int paragraphs, int tables, int pictures, int shapes, string reference, string text)
            {
                PartPath = partPath;
                Kind = kind;
                Role = role;
                Paragraphs = paragraphs;
                Tables = tables;
                Pictures = pictures;
                Shapes = shapes;
                Reference = reference ?? string.Empty;
                Text = text ?? string.Empty;
            }

            public string PartPath { get; private set; }

            public string Kind { get; private set; }

            public string Role { get; private set; }

            public int Paragraphs { get; private set; }

            public int Tables { get; private set; }

            public int Pictures { get; private set; }

            public int Shapes { get; private set; }

            public string Reference { get; private set; }

            public string Text { get; private set; }
        }

        internal sealed class FieldFormDetail
        {
            public FieldFormDetail(string partPath, string kind, string attributes, string text)
            {
                PartPath = partPath;
                Kind = kind;
                Attributes = attributes ?? string.Empty;
                Text = text ?? string.Empty;
            }

            public string PartPath { get; private set; }

            public string Kind { get; private set; }

            public string Attributes { get; private set; }

            public string Text { get; private set; }
        }

        internal sealed class ReferenceDetail
        {
            public ReferenceDetail(string partPath, string kind, string attributes, string text)
            {
                PartPath = partPath;
                Kind = kind;
                Attributes = attributes ?? string.Empty;
                Text = text ?? string.Empty;
            }

            public string PartPath { get; private set; }

            public string Kind { get; private set; }

            public string Attributes { get; private set; }

            public string Text { get; private set; }
        }
    }
}
