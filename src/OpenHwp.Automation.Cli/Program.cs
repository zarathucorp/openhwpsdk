using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenHwp.Automation;

namespace OpenHwp.Automation.Cli
{
    internal static class Program
    {
        private const int DefaultComTimeoutMs = 60000;
        private static int _comTimeoutMs = DefaultComTimeoutMs;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(true);
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            var visible = false;
            var keepOpen = false;
            var offset = 0;

            while (args.Length > offset)
            {
                if (string.Equals(args[offset], "--visible", StringComparison.OrdinalIgnoreCase))
                {
                    visible = true;
                    offset++;
                    continue;
                }

                if (string.Equals(args[offset], "--keep-open", StringComparison.OrdinalIgnoreCase))
                {
                    keepOpen = true;
                    offset++;
                    continue;
                }

                if (string.Equals(args[offset], "--com-timeout-ms", StringComparison.OrdinalIgnoreCase))
                {
                    if (offset + 1 >= args.Length || !int.TryParse(args[offset + 1], out _comTimeoutMs) || _comTimeoutMs < 0)
                    {
                        Console.Error.WriteLine("--com-timeout-ms requires a zero or positive integer value.");
                        return 1;
                    }

                    offset += 2;
                    continue;
                }

                if (string.Equals(args[offset], "--no-com-timeout", StringComparison.OrdinalIgnoreCase))
                {
                    _comTimeoutMs = 0;
                    offset++;
                    continue;
                }

                break;
            }

            if (args.Length <= offset)
            {
                PrintUsage();
                return 1;
            }

            var command = args[offset].ToLowerInvariant();
            var commandArgs = new string[args.Length - offset];
            Array.Copy(args, offset, commandArgs, 0, commandArgs.Length);

            switch (command)
            {
                case "version":
                    return Version(visible, keepOpen);
                case "diagnose-com":
                    return DiagnoseCom(commandArgs, visible, keepOpen);
                case "new-text":
                    return NewText(commandArgs, visible, keepOpen);
                case "copy-save":
                    return CopySave(commandArgs, visible, keepOpen);
                case "export-pdf":
                    return ExportPdf(commandArgs, visible, keepOpen);
                case "doc-info":
                    return DocInfo(commandArgs, visible, keepOpen);
                case "read-text":
                    return ReadText(commandArgs, visible, keepOpen);
                case "read-page":
                    return ReadPage(commandArgs, visible, keepOpen);
                case "field-exists":
                    return FieldExists(commandArgs, visible, keepOpen);
                case "field-get":
                    return FieldGet(commandArgs, visible, keepOpen);
                case "field-list-raw":
                    return FieldListRaw(commandArgs, visible, keepOpen);
                case "list-fields":
                    return ListFields(commandArgs, visible, keepOpen);
                case "field-set":
                    return FieldSet(commandArgs, visible, keepOpen);
                case "replace-markdown":
                    return ReplaceMarkdown(commandArgs, visible, keepOpen);
                case "append-markdown-lines":
                    return AppendMarkdownLines(commandArgs, visible, keepOpen);
                case "markdown-submission-text":
                    return MarkdownSubmissionText(commandArgs);
                case "markdown-table-list":
                    return MarkdownTableList(commandArgs);
                case "table-create-package":
                    return TableCreatePackage(commandArgs);
                case "table-row-package":
                    return TableRowPackage(commandArgs);
                case "table-column-package":
                    return TableColumnPackage(commandArgs);
                case "table-merge-package":
                    return TableMergePackage(commandArgs);
                case "table-split-package":
                    return TableSplitPackage(commandArgs);
                case "table-cell-style-package":
                    return TableCellStylePackage(commandArgs);
                case "table-cell-align-package":
                    return TableCellAlignPackage(commandArgs);
                case "table-cell-background-package":
                    return TableCellBackgroundPackage(commandArgs);
                case "table-cell-diagonal-package":
                    return TableCellDiagonalPackage(commandArgs);
                case "table-cell-size-package":
                    return TableCellSizePackage(commandArgs);
                case "table-cell-set":
                    return TableCellSet(commandArgs, visible, keepOpen);
                case "fill-markdown-table":
                    return FillMarkdownTable(commandArgs, visible, keepOpen);
                case "fill-submission-template":
                    return FillSubmissionTemplate(commandArgs, visible, keepOpen);
                case "extract-form-map":
                    return ExtractFormMap(commandArgs);
                case "apply-form-map":
                    return ApplyFormMap(commandArgs, visible, keepOpen);
                case "probe-form-map":
                    return ProbeFormMap(commandArgs, visible, keepOpen);
                case "validate-layout":
                    return ValidateLayout(commandArgs);
                case "validate-content":
                    return ValidateContent(commandArgs);
                case "scan-hwpx-features":
                    return ScanHwpxFeatures(commandArgs);
                case "list-pictures":
                    return ListPictures(commandArgs);
                case "replace-image-control":
                    return ReplaceImageControl(commandArgs);
                case "list-header-footer":
                    return ListHeaderFooter(commandArgs);
                case "set-header-footer-text":
                    return SetHeaderFooterText(commandArgs);
                case "page-number-set":
                    return PageNumberSet(commandArgs, visible, keepOpen);
                case "list-controls":
                    return ListControls(commandArgs, visible, keepOpen);
                case "probe-copy-from-doc":
                    return ProbeCopyFromDoc(commandArgs, visible, keepOpen);
                case "copy-from-doc":
                    return CopyFromDoc(commandArgs, visible, keepOpen);
                case "replace-after-marker":
                    return ReplaceAfterMarker(commandArgs, visible, keepOpen);
                case "replace-text":
                    return ReplaceText(commandArgs, visible, keepOpen);
                case "replace-text-batch":
                    return ReplaceTextBatch(commandArgs, visible, keepOpen);
                case "demo-list":
                    return DemoList();
                case "demo-feature":
                    return DemoFeature(commandArgs, visible, keepOpen);
                case "run":
                    return RunCommand(commandArgs, visible, keepOpen);
                case "action":
                    return Action(commandArgs, visible, keepOpen);
                default:
                    Console.Error.WriteLine($"Unknown command: {commandArgs[0]}");
                    PrintUsage();
                    return 1;
            }
        }

        private static int Version(bool visible, bool keepOpen)
        {
            using (var hwp = CreateSession(visible, keepOpen))
            {
                Console.WriteLine(hwp.Version);
                return 0;
            }
        }

        private static int DiagnoseCom(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length > 2)
            {
                Console.Error.WriteLine("Usage: diagnose-com [inputPath]");
                return 1;
            }

            Console.WriteLine("prog_id=" + HwpSession.HwpObjectProgId);
            Console.WriteLine("running_hwp_processes=" + ComOperationWatchdog.DescribeHwpProcesses());

            Type comType;
            using (var watchdog = ComOperationWatchdog.Start("resolve HWP COM registration", _comTimeoutMs))
            {
                watchdog.Step("Resolve HWP COM ProgID");
                comType = Type.GetTypeFromProgID(HwpSession.HwpObjectProgId, false);
            }

            Console.WriteLine("com_registered=" + BoolText(comType != null));
            if (comType == null)
            {
                Console.WriteLine("last_step=Resolve HWP COM ProgID");
                return 2;
            }

            Console.WriteLine("com_type=" + comType.FullName);

            HwpSession hwp = null;
            try
            {
                hwp = CreateSession(visible, keepOpen);
                Console.WriteLine("create_session=ok");
                Console.WriteLine("last_step=Create HWP session");

                using (var watchdog = ComOperationWatchdog.Start("read HWP COM properties", _comTimeoutMs))
                {
                    watchdog.Step("Read HWP version");
                    Console.WriteLine("version=" + hwp.Version);

                    watchdog.Step("Read active window visibility");
                    Console.WriteLine("visible=" + BoolText(hwp.Visible));
                }

                using (var watchdog = ComOperationWatchdog.Start("configure HWP automation diagnostics", _comTimeoutMs))
                {
                    watchdog.Step("Register file path checker module");
                    Console.WriteLine("file_path_checker_registered=" + BoolText(hwp.TryRegisterFilePathCheckerModule()));

                    watchdog.Step("Read message box mode before update");
                    Console.WriteLine("message_box_mode_before=" + hwp.GetMessageBoxMode());

                    watchdog.Step("Set automation message box mode");
                    Console.WriteLine("message_box_mode_set_result=" + hwp.SetMessageBoxMode(0x10));

                    watchdog.Step("Read message box mode after update");
                    Console.WriteLine("message_box_mode_after=" + hwp.GetMessageBoxMode());
                }

                if (args.Length == 2)
                {
                    OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");
                    Console.WriteLine("open=ok");
                    Console.WriteLine("current_path=" + hwp.CurrentPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("diagnostic_failed=true");
                Console.WriteLine("error_type=" + ex.GetType().Name);
                Console.WriteLine("error_message=" + ex.Message);
                return 2;
            }
            finally
            {
                if (hwp != null)
                {
                    hwp.Dispose();
                }
            }

            Console.WriteLine("last_step=complete");
            return 0;
        }

        private static int NewText(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: new-text <outputPath> <text>");
                return 1;
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                hwp.InsertText(args[2]);
                hwp.SaveAs(args[1], string.Empty, string.Empty);
            }

            Console.WriteLine(args[1]);
            return 0;
        }

        private static int CopySave(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: copy-save <inputPath> <outputPath>");
                return 1;
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1]);
                hwp.SaveAs(args[2], string.Empty, string.Empty);
            }

            Console.WriteLine(args[2]);
            return 0;
        }

        private static int ExportPdf(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: export-pdf <inputPath> <outputPdfPath>");
                return 1;
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");
                hwp.SaveAs(args[2], "PDF", string.Empty);
            }

            Console.WriteLine(args[2]);
            return 0;
        }

        private static int DocInfo(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: doc-info <inputPath>");
                return 1;
            }

            using (var hwp = OpenDocument(args[1], visible, keepOpen))
            {
                Console.WriteLine("version=" + hwp.Version);
                Console.WriteLine("path=" + hwp.CurrentPath);
                Console.WriteLine("page_count=" + hwp.PageCount);
                Console.WriteLine("visible=" + hwp.Visible.ToString().ToLowerInvariant());
            }

            return 0;
        }

        private static int ReadText(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: read-text <inputPath> [outputPath]");
                return 1;
            }

            using (var hwp = OpenDocument(args[1], visible, keepOpen))
            {
                var text = hwp.GetDocumentText();
                return WriteTextResult(text, args.Length >= 3 ? args[2] : null);
            }
        }

        private static int ReadPage(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: read-page <inputPath> <pageNumber> [outputPath]");
                return 1;
            }

            int pageNumber;
            if (!int.TryParse(args[2], out pageNumber))
            {
                Console.Error.WriteLine("pageNumber must be an integer.");
                return 1;
            }

            using (var hwp = OpenDocument(args[1], visible, keepOpen))
            {
                var text = hwp.GetPageText(pageNumber);
                return WriteTextResult(text, args.Length >= 4 ? args[3] : null);
            }
        }

        private static int FieldExists(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: field-exists <inputPath> <fieldName>");
                return 1;
            }

            using (var hwp = OpenDocument(args[1], visible, keepOpen))
            {
                Console.WriteLine(hwp.FieldExists(args[2]) ? "true" : "false");
            }

            return 0;
        }

        private static int FieldGet(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: field-get <inputPath> <fieldName>");
                return 1;
            }

            using (var hwp = OpenDocument(args[1], visible, keepOpen))
            {
                var exists = hwp.FieldExists(args[2]);
                Console.WriteLine("field_exists=" + exists.ToString().ToLowerInvariant());
                Console.WriteLine("field_name=" + args[2]);
                if (exists)
                {
                    Console.WriteLine("field_text=" + hwp.GetFieldText(args[2]));
                }
            }

            return 0;
        }

        private static int FieldListRaw(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: field-list-raw <inputPath> [option] [optionEx]");
                return 1;
            }

            var option = 0;
            var optionEx = 0;

            if (args.Length >= 3 && !int.TryParse(args[2], out option))
            {
                Console.Error.WriteLine("option must be an integer.");
                return 1;
            }

            if (args.Length >= 4 && !int.TryParse(args[3], out optionEx))
            {
                Console.Error.WriteLine("optionEx must be an integer.");
                return 1;
            }

            using (var hwp = OpenDocument(args[1], visible, keepOpen))
            {
                Console.WriteLine(hwp.GetFieldListRaw(option, optionEx));
            }

            return 0;
        }

        private static int ListFields(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: list-fields <hwpxFileOrDirectory> [reportMarkdownPath] [--com] [--option value] [--option-ex value]");
                return 1;
            }

            string reportPath = null;
            var includeCom = false;
            var option = 0;
            var optionEx = 0;

            for (var index = 2; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--com", StringComparison.OrdinalIgnoreCase))
                {
                    includeCom = true;
                    continue;
                }

                if (string.Equals(args[index], "--option", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out option))
                    {
                        Console.Error.WriteLine("--option requires an integer value.");
                        return 1;
                    }

                    index++;
                    continue;
                }

                if (string.Equals(args[index], "--option-ex", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out optionEx))
                    {
                        Console.Error.WriteLine("--option-ex requires an integer value.");
                        return 1;
                    }

                    index++;
                    continue;
                }

                if (reportPath == null)
                {
                    reportPath = args[index];
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (includeCom && !File.Exists(args[1]))
            {
                Console.Error.WriteLine("--com can be used only with a single HWP/HWPX file.");
                return 1;
            }

            var summary = HwpxFeatureScanner.Scan(args[1]);
            var comRaw = string.Empty;
            var comFields = new List<ComFieldInventoryItem>();
            if (includeCom)
            {
                using (var hwp = OpenDocument(args[1], visible, keepOpen))
                {
                    comRaw = hwp.GetFieldListRaw(option, optionEx) ?? string.Empty;
                    foreach (var name in SplitComFieldNames(comRaw))
                    {
                        var item = new ComFieldInventoryItem { Name = name };
                        try
                        {
                            item.Exists = hwp.FieldExists(name);
                            item.Text = item.Exists ? hwp.GetFieldText(name) : string.Empty;
                        }
                        catch (Exception ex)
                        {
                            item.Error = ex.Message;
                        }

                        comFields.Add(item);
                    }
                }
            }

            var packageItems = summary.Files.Sum(file => file.FieldFormItems.Count);
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                WriteFieldInventoryReport(summary, includeCom, comRaw, comFields, option, optionEx, reportPath);
            }

            Console.WriteLine("input=" + summary.InputPath);
            Console.WriteLine("files=" + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("package_errors=" + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("xml_parse_errors=" + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("package_field_items=" + packageItems.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("field_markers=" + summary.Count("fieldMarkers").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("press_fields=" + summary.Count("pressFields").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("form_objects=" + summary.Count("formObjects").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("com_enabled=" + BoolText(includeCom));
            Console.WriteLine("com_fields=" + comFields.Count.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                Console.WriteLine(reportPath);
            }

            return 0;
        }

        private static int FieldSet(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 5)
            {
                Console.Error.WriteLine("Usage: field-set <inputPath> <fieldName> <text> <outputPath>");
                return 1;
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1]);
                hwp.PutFieldText(args[2], args[3]);
                hwp.SaveAs(args[4], string.Empty, string.Empty);
            }

            Console.WriteLine(args[4]);
            return 0;
        }

        private static int ReplaceText(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 5)
            {
                Console.Error.WriteLine("Usage: replace-text <inputPath> <findText> <replaceText> <outputPath>");
                return 1;
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1]);

                if (!hwp.ReplaceAllText(args[2], args[3]))
                {
                    Console.Error.WriteLine("Find text was not found.");
                    return 2;
                }

                hwp.SaveAs(args[4], string.Empty, string.Empty);
            }

            Console.WriteLine(args[4]);
            return 0;
        }

        private static int ReplaceTextBatch(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 5 || ((args.Length - 3) % 2) != 0)
            {
                Console.Error.WriteLine("Usage: replace-text-batch <inputPath> <outputPath> <findText1> <replaceText1> [<findText2> <replaceText2> ...]");
                return 1;
            }

            var missingReplacements = new List<string>();
            var replacementCount = (args.Length - 3) / 2;

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1]);

                for (var index = 3; index < args.Length; index += 2)
                {
                    var findText = args[index];
                    var replaceText = args[index + 1];
                    var step = ((index - 3) / 2) + 1;

                    Console.WriteLine(
                        "[{0}/{1}] replace '{2}' -> '{3}'",
                        step,
                        replacementCount,
                        Abbreviate(findText),
                        Abbreviate(replaceText));

                    if (!hwp.ReplaceAllText(findText, replaceText))
                    {
                        missingReplacements.Add(findText);
                        Console.WriteLine("  not found");
                        continue;
                    }

                    Console.WriteLine("  ok");
                }

                hwp.SaveAs(args[2], string.Empty, string.Empty);
            }

            Console.WriteLine(args[2]);

            if (missingReplacements.Count == 0)
            {
                return 0;
            }

            Console.Error.WriteLine("Not found: " + string.Join(" | ", missingReplacements.ToArray()));
            return 2;
        }

        private static int DemoFeature(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: demo-feature <featureName> [key=value ...] [--open <path>] [--save <path>]");
                return 1;
            }

            var featureName = args[1];
            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            string openPath = null;
            string savePath = null;

            for (var i = 2; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--open", StringComparison.OrdinalIgnoreCase))
                {
                    openPath = RequireValue(args, ref i, "--open");
                    continue;
                }

                if (string.Equals(args[i], "--save", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = RequireValue(args, ref i, "--save");
                    continue;
                }

                var separatorIndex = args[i].IndexOf('=');
                if (separatorIndex <= 0)
                {
                    Console.Error.WriteLine($"Expected key=value pair, got '{args[i]}'.");
                    return 1;
                }

                parameters[args[i].Substring(0, separatorIndex)] = ParseValue(args[i].Substring(separatorIndex + 1));
            }

            if (string.Equals(featureName, "insertpicture", StringComparison.OrdinalIgnoreCase))
            {
                HwpSession.ValidateInsertPictureDimensions(
                    GetInt(parameters, new[] { "width" }, 200),
                    GetInt(parameters, new[] { "height" }, 200));
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);

                if (!string.IsNullOrWhiteSpace(openPath))
                {
                    OpenSessionDocument(hwp, openPath);
                }

                var executed = ExecuteDemoFeature(hwp, featureName, parameters);
                Console.WriteLine(executed ? "true" : "false");

                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    hwp.SaveAs(savePath, string.Empty, string.Empty);
                    Console.WriteLine(savePath);
                }

                return executed ? 0 : 2;
            }
        }

        private static int ReplaceMarkdown(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: replace-markdown <inputPath> <markdownPath> <outputPath>");
                return 1;
            }

            Console.Error.WriteLine("Warning: replace-markdown rewrites the document as plain text and does not preserve tables, shapes, or character styles.");
            var markdown = ReadTextFile(args[2]);
            var plainText = MarkdownTextConverter.ToPlainText(markdown);

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1]);
                hwp.ReplaceDocumentText(plainText);
                hwp.SaveAs(args[3], ResolveSaveFormat(args[3]), string.Empty);
            }

            Console.WriteLine(args[3]);
            return 0;
        }

        private static int AppendMarkdownLines(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: append-markdown-lines <inputPath> <markdownPath> <outputPath> [maxLines]");
                return 1;
            }

            var maxLines = 0;
            var hasMaxLines = args.Length >= 5 && int.TryParse(args[4], out maxLines) && maxLines > 0;
            var markdown = ReadTextFile(args[2]);
            var plainText = MarkdownTextConverter.ToPlainText(markdown);
            var lines = plainText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var inserted = 0;

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");
                hwp.Run("MoveDocEnd");
                hwp.SetCharShapeHeight(1000, false, 0);

                foreach (var line in lines)
                {
                    if (hasMaxLines && inserted >= maxLines)
                    {
                        break;
                    }

                    hwp.InsertText((line ?? string.Empty) + Environment.NewLine);
                    inserted++;
                }

                hwp.SaveAs(args[3], ResolveSaveFormat(args[3]), string.Empty);
            }

            Console.WriteLine(args[3]);
            Console.WriteLine("inserted_lines=" + inserted);
            return 0;
        }

        private static int MarkdownTableList(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: markdown-table-list <markdownPath>");
                return 1;
            }

            var markdown = ReadTextFile(args[1]);
            var tables = MarkdownTableParser.ParseTables(markdown);
            Console.WriteLine("tables=" + tables.Count);

            for (var tableIndex = 0; tableIndex < tables.Count; tableIndex++)
            {
                var table = tables[tableIndex];
                var maxColumns = 0;

                for (var rowIndex = 0; rowIndex < table.Count; rowIndex++)
                {
                    if (table[rowIndex].Count > maxColumns)
                    {
                        maxColumns = table[rowIndex].Count;
                    }
                }

                var preview = table.Count > 0 ? string.Join(" | ", table[0]) : string.Empty;
                Console.WriteLine(
                    "[{0}] rows={1} max_cols={2} first_row={3}",
                    tableIndex,
                    table.Count,
                    maxColumns,
                    Abbreviate(preview, 120));
            }

            return 0;
        }

        private static int MarkdownSubmissionText(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: markdown-submission-text <markdownPath> [outputPath]");
                return 1;
            }

            var markdown = ReadTextFile(args[1]);
            var text = MarkdownTextConverter.ToSubmissionPlainText(markdown);
            return WriteTextResult(text, args.Length >= 3 ? args[2] : null);
        }

        private static int TableCellSet(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 7)
            {
                Console.Error.WriteLine("Usage: table-cell-set <inputPath> <outputPath> <tableIndex> <rowMoveCount> <columnMoveCount> <text>");
                return 1;
            }

            var tableIndex = ParseIntArgument(args[3], "tableIndex");
            var rowMoveCount = ParseIntArgument(args[4], "rowMoveCount");
            var columnMoveCount = ParseIntArgument(args[5], "columnMoveCount");
            if (tableIndex < 0 || rowMoveCount < 0 || columnMoveCount < 0)
            {
                Console.Error.WriteLine("tableIndex, rowMoveCount, and columnMoveCount must be zero or greater.");
                return 1;
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");

                if (!hwp.SetTableCellText(tableIndex, rowMoveCount, columnMoveCount, args[6]))
                {
                    Console.Error.WriteLine("Target table cell was not selected.");
                    return 2;
                }

                hwp.SaveAs(args[2], ResolveSaveFormat(args[2]), string.Empty);
            }

            Console.WriteLine(args[2]);
            Console.WriteLine("table_index=" + tableIndex);
            Console.WriteLine("row_move_count=" + rowMoveCount);
            Console.WriteLine("column_move_count=" + columnMoveCount);
            return 0;
        }

        private static int TableCreatePackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-create-package <inputHwpxPath> <outputHwpxPath> --rows count --cols count [--text row1col1|row1col2;row2col1|row2col2] [--text-file path] [--section sectionName] [--after-anchor text] [--reference-table index] [--border-fill-id id] [--header-border-fill-id id] [--report reportMarkdownPath]");
                return 1;
            }

            var options = new TableCreateOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0"
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--rows", StringComparison.OrdinalIgnoreCase))
                {
                    options.Rows = ParseIntArgument(RequireValue(args, ref index, "--rows"), "rows");
                    continue;
                }

                if (string.Equals(args[index], "--cols", StringComparison.OrdinalIgnoreCase))
                {
                    options.Columns = ParseIntArgument(RequireValue(args, ref index, "--cols"), "cols");
                    continue;
                }

                if (string.Equals(args[index], "--text", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = RequireValue(args, ref index, "--text");
                    continue;
                }

                if (string.Equals(args[index], "--text-file", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = ReadTextFile(RequireValue(args, ref index, "--text-file"));
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--after-anchor", StringComparison.OrdinalIgnoreCase))
                {
                    options.AnchorText = RequireValue(args, ref index, "--after-anchor");
                    continue;
                }

                if (string.Equals(args[index], "--reference-table", StringComparison.OrdinalIgnoreCase))
                {
                    var referenceTableIndex = ParseIntArgument(RequireValue(args, ref index, "--reference-table"), "reference-table");
                    if (referenceTableIndex < 0)
                    {
                        Console.Error.WriteLine("--reference-table must be zero or greater.");
                        return 1;
                    }

                    options.ReferenceTableIndex = referenceTableIndex;
                    continue;
                }

                if (string.Equals(args[index], "--border-fill-id", StringComparison.OrdinalIgnoreCase))
                {
                    var borderFillId = ParseIntArgument(RequireValue(args, ref index, "--border-fill-id"), "border-fill-id");
                    if (borderFillId < 0)
                    {
                        Console.Error.WriteLine("--border-fill-id must be zero or greater.");
                        return 1;
                    }

                    options.BorderFillId = borderFillId;
                    continue;
                }

                if (string.Equals(args[index], "--header-border-fill-id", StringComparison.OrdinalIgnoreCase))
                {
                    var headerBorderFillId = ParseIntArgument(RequireValue(args, ref index, "--header-border-fill-id"), "header-border-fill-id");
                    if (headerBorderFillId < 0)
                    {
                        Console.Error.WriteLine("--header-border-fill-id must be zero or greater.");
                        return 1;
                    }

                    options.HeaderBorderFillId = headerBorderFillId;
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.Rows <= 0 || options.Columns <= 0)
            {
                Console.Error.WriteLine("--rows and --cols are required and must be greater than zero.");
                return 1;
            }

            var result = HwpxTableCreator.Create(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("rows=" + result.Rows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("columns=" + result.Columns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_top_level_table_count=" + result.OriginalTableCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_top_level_table_count=" + result.NewTableCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("reference_table_index=" + (result.ReferenceTableIndex.HasValue ? result.ReferenceTableIndex.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
            Console.WriteLine("usable_reference_candidates=" + result.UsableReferenceCandidateCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("missing_text_cells=" + result.MissingTextCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("ignored_text_cells=" + result.IgnoredTextCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("style_repaired_runs=" + result.StyleRepairedRuns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("inserted_at=" + result.InsertedAt);
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableRowPackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-row-package <inputHwpxPath> <outputHwpxPath> --table-index index --action add|delete [--row index] [--count count] [--text row1col1|row1col2;row2col1|row2col2] [--text-file path] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  For add, --row means insert after that zero-based row; omit it to append after the last row. For delete, --row is the zero-based first row to delete.");
                return 1;
            }

            var options = new TableRowOperationOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1,
                Count = 1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--action", StringComparison.OrdinalIgnoreCase))
                {
                    options.Action = RequireValue(args, ref index, "--action");
                    continue;
                }

                if (string.Equals(args[index], "--row", StringComparison.OrdinalIgnoreCase))
                {
                    options.RowIndex = ParseIntArgument(RequireValue(args, ref index, "--row"), "row");
                    continue;
                }

                if (string.Equals(args[index], "--count", StringComparison.OrdinalIgnoreCase))
                {
                    options.Count = ParseIntArgument(RequireValue(args, ref index, "--count"), "count");
                    continue;
                }

                if (string.Equals(args[index], "--text", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = RequireValue(args, ref index, "--text");
                    continue;
                }

                if (string.Equals(args[index], "--text-file", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = ReadTextFile(RequireValue(args, ref index, "--text-file"));
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (options.Count <= 0)
            {
                Console.Error.WriteLine("--count must be greater than zero.");
                return 1;
            }

            var action = (options.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "add" && action != "delete")
            {
                Console.Error.WriteLine("--action must be add or delete.");
                return 1;
            }

            if (action == "delete" && !options.RowIndex.HasValue)
            {
                Console.Error.WriteLine("--row is required when --action delete.");
                return 1;
            }

            var result = HwpxTableRowEditor.Apply(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("action=" + result.Action);
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("effective_row=" + (result.EffectiveRow.HasValue ? result.EffectiveRow.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("added_rows=" + result.AddedRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("deleted_rows=" + result.DeletedRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("missing_text_cells=" + result.MissingTextCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("ignored_text_cells=" + result.IgnoredTextCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("style_repaired_runs=" + result.StyleRepairedRuns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableColumnPackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-column-package <inputHwpxPath> <outputHwpxPath> --table-index index --action add|delete [--column index] [--count count] [--text row1col1|row1col2;row2col1|row2col2] [--text-file path] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  For add, --column means insert after that zero-based column; omit it to append after the last column. For delete, --column is the zero-based first column to delete.");
                return 1;
            }

            var options = new TableColumnOperationOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1,
                Count = 1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--action", StringComparison.OrdinalIgnoreCase))
                {
                    options.Action = RequireValue(args, ref index, "--action");
                    continue;
                }

                if (string.Equals(args[index], "--column", StringComparison.OrdinalIgnoreCase))
                {
                    options.ColumnIndex = ParseIntArgument(RequireValue(args, ref index, "--column"), "column");
                    continue;
                }

                if (string.Equals(args[index], "--count", StringComparison.OrdinalIgnoreCase))
                {
                    options.Count = ParseIntArgument(RequireValue(args, ref index, "--count"), "count");
                    continue;
                }

                if (string.Equals(args[index], "--text", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = RequireValue(args, ref index, "--text");
                    continue;
                }

                if (string.Equals(args[index], "--text-file", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = ReadTextFile(RequireValue(args, ref index, "--text-file"));
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (options.Count <= 0)
            {
                Console.Error.WriteLine("--count must be greater than zero.");
                return 1;
            }

            var action = (options.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "add" && action != "delete")
            {
                Console.Error.WriteLine("--action must be add or delete.");
                return 1;
            }

            if (action == "delete" && !options.ColumnIndex.HasValue)
            {
                Console.Error.WriteLine("--column is required when --action delete.");
                return 1;
            }

            var result = HwpxTableColumnEditor.Apply(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("action=" + result.Action);
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("effective_column=" + (result.EffectiveColumn.HasValue ? result.EffectiveColumn.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("added_columns=" + result.AddedColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("deleted_columns=" + result.DeletedColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("missing_text_cells=" + result.MissingTextCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("ignored_text_cells=" + result.IgnoredTextCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("style_repaired_runs=" + result.StyleRepairedRuns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableMergePackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-merge-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index --row-span count --col-span count [--text text] [--text-file path] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  --row and --column identify the zero-based top-left cell of the rectangular merge region.");
                return 1;
            }

            var options = new TableMergeOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1,
                Row = -1,
                Column = -1,
                RowSpan = 1,
                ColumnSpan = 1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--row", StringComparison.OrdinalIgnoreCase))
                {
                    options.Row = ParseIntArgument(RequireValue(args, ref index, "--row"), "row");
                    continue;
                }

                if (string.Equals(args[index], "--column", StringComparison.OrdinalIgnoreCase))
                {
                    options.Column = ParseIntArgument(RequireValue(args, ref index, "--column"), "column");
                    continue;
                }

                if (string.Equals(args[index], "--row-span", StringComparison.OrdinalIgnoreCase))
                {
                    options.RowSpan = ParseIntArgument(RequireValue(args, ref index, "--row-span"), "row-span");
                    continue;
                }

                if (string.Equals(args[index], "--col-span", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--column-span", StringComparison.OrdinalIgnoreCase))
                {
                    var flagName = args[index];
                    options.ColumnSpan = ParseIntArgument(RequireValue(args, ref index, flagName), flagName.TrimStart('-'));
                    continue;
                }

                if (string.Equals(args[index], "--text", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = RequireValue(args, ref index, "--text");
                    continue;
                }

                if (string.Equals(args[index], "--text-file", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = ReadTextFile(RequireValue(args, ref index, "--text-file"));
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (options.Row < 0)
            {
                Console.Error.WriteLine("--row is required and must be zero or greater.");
                return 1;
            }

            if (options.Column < 0)
            {
                Console.Error.WriteLine("--column is required and must be zero or greater.");
                return 1;
            }

            if (options.RowSpan <= 0 || options.ColumnSpan <= 0)
            {
                Console.Error.WriteLine("--row-span and --col-span must be greater than zero.");
                return 1;
            }

            if (options.RowSpan == 1 && options.ColumnSpan == 1)
            {
                Console.Error.WriteLine("merge rectangle must cover more than one cell.");
                return 1;
            }

            var result = HwpxTableMergeEditor.Merge(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row=" + result.Row.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column=" + result.Column.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row_span=" + result.RowSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column_span=" + result.ColumnSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("removed_cells=" + result.RemovedCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("merged_text_length=" + result.MergedTextLength.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("style_repaired_runs=" + result.StyleRepairedRuns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableSplitPackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-split-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index [--text row1col1|row1col2;row2col1|row2col2] [--text-file path] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  --row and --column identify the zero-based top-left address of the merged cell to split.");
                return 1;
            }

            var options = new TableSplitOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1,
                Row = -1,
                Column = -1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--row", StringComparison.OrdinalIgnoreCase))
                {
                    options.Row = ParseIntArgument(RequireValue(args, ref index, "--row"), "row");
                    continue;
                }

                if (string.Equals(args[index], "--column", StringComparison.OrdinalIgnoreCase))
                {
                    options.Column = ParseIntArgument(RequireValue(args, ref index, "--column"), "column");
                    continue;
                }

                if (string.Equals(args[index], "--text", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = RequireValue(args, ref index, "--text");
                    continue;
                }

                if (string.Equals(args[index], "--text-file", StringComparison.OrdinalIgnoreCase))
                {
                    options.Text = ReadTextFile(RequireValue(args, ref index, "--text-file"));
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (options.Row < 0)
            {
                Console.Error.WriteLine("--row is required and must be zero or greater.");
                return 1;
            }

            if (options.Column < 0)
            {
                Console.Error.WriteLine("--column is required and must be zero or greater.");
                return 1;
            }

            var result = HwpxTableSplitEditor.Split(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row=" + result.Row.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column=" + result.Column.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row_span=" + result.RowSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column_span=" + result.ColumnSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_physical_cells=" + result.OriginalPhysicalCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_physical_cells=" + result.NewPhysicalCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("added_cells=" + result.AddedCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("missing_text_cells=" + result.MissingTextCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("ignored_text_cells=" + result.IgnoredTextCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("style_repaired_runs=" + result.StyleRepairedRuns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableCellStylePackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-cell-style-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index --border-fill-id id [--row-span count] [--col-span count] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  The rectangle applies to all cells whose cell span intersects the selected zero-based row/column range.");
                return 1;
            }

            var options = new TableCellStyleOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1,
                Row = -1,
                Column = -1,
                RowSpan = 1,
                ColumnSpan = 1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--row", StringComparison.OrdinalIgnoreCase))
                {
                    options.Row = ParseIntArgument(RequireValue(args, ref index, "--row"), "row");
                    continue;
                }

                if (string.Equals(args[index], "--column", StringComparison.OrdinalIgnoreCase))
                {
                    options.Column = ParseIntArgument(RequireValue(args, ref index, "--column"), "column");
                    continue;
                }

                if (string.Equals(args[index], "--row-span", StringComparison.OrdinalIgnoreCase))
                {
                    options.RowSpan = ParseIntArgument(RequireValue(args, ref index, "--row-span"), "row-span");
                    continue;
                }

                if (string.Equals(args[index], "--col-span", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--column-span", StringComparison.OrdinalIgnoreCase))
                {
                    var flagName = args[index];
                    options.ColumnSpan = ParseIntArgument(RequireValue(args, ref index, flagName), flagName.TrimStart('-'));
                    continue;
                }

                if (string.Equals(args[index], "--border-fill-id", StringComparison.OrdinalIgnoreCase))
                {
                    options.BorderFillId = ParseIntArgument(RequireValue(args, ref index, "--border-fill-id"), "border-fill-id");
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (options.Row < 0)
            {
                Console.Error.WriteLine("--row is required and must be zero or greater.");
                return 1;
            }

            if (options.Column < 0)
            {
                Console.Error.WriteLine("--column is required and must be zero or greater.");
                return 1;
            }

            if (options.RowSpan <= 0 || options.ColumnSpan <= 0)
            {
                Console.Error.WriteLine("--row-span and --col-span must be greater than zero.");
                return 1;
            }

            if (!options.BorderFillId.HasValue)
            {
                Console.Error.WriteLine("--border-fill-id is required.");
                return 1;
            }

            var result = HwpxTableCellStyleEditor.Apply(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row=" + result.Row.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column=" + result.Column.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row_span=" + result.RowSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column_span=" + result.ColumnSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("border_fill_id=" + (result.BorderFillId.HasValue ? result.BorderFillId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
            Console.WriteLine("affected_cells=" + result.AffectedCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("affected_cell_addresses=" + result.AffectedCellAddresses);
            Console.WriteLine("previous_border_fill_ids=" + result.PreviousBorderFillIds);
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableCellAlignPackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-cell-align-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index [--horizontal left|center|right|justify] [--vertical top|center|bottom] [--row-span count] [--col-span count] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  Horizontal alignment clones the referenced paraPr in header.xml and points affected cell paragraphs at the clone.");
                return 1;
            }

            var options = new TableCellAlignOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1,
                Row = -1,
                Column = -1,
                RowSpan = 1,
                ColumnSpan = 1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--row", StringComparison.OrdinalIgnoreCase))
                {
                    options.Row = ParseIntArgument(RequireValue(args, ref index, "--row"), "row");
                    continue;
                }

                if (string.Equals(args[index], "--column", StringComparison.OrdinalIgnoreCase))
                {
                    options.Column = ParseIntArgument(RequireValue(args, ref index, "--column"), "column");
                    continue;
                }

                if (string.Equals(args[index], "--row-span", StringComparison.OrdinalIgnoreCase))
                {
                    options.RowSpan = ParseIntArgument(RequireValue(args, ref index, "--row-span"), "row-span");
                    continue;
                }

                if (string.Equals(args[index], "--col-span", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--column-span", StringComparison.OrdinalIgnoreCase))
                {
                    var flagName = args[index];
                    options.ColumnSpan = ParseIntArgument(RequireValue(args, ref index, flagName), flagName.TrimStart('-'));
                    continue;
                }

                if (string.Equals(args[index], "--horizontal", StringComparison.OrdinalIgnoreCase))
                {
                    options.Horizontal = RequireValue(args, ref index, "--horizontal");
                    continue;
                }

                if (string.Equals(args[index], "--vertical", StringComparison.OrdinalIgnoreCase))
                {
                    options.Vertical = RequireValue(args, ref index, "--vertical");
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (options.Row < 0)
            {
                Console.Error.WriteLine("--row is required and must be zero or greater.");
                return 1;
            }

            if (options.Column < 0)
            {
                Console.Error.WriteLine("--column is required and must be zero or greater.");
                return 1;
            }

            if (options.RowSpan <= 0 || options.ColumnSpan <= 0)
            {
                Console.Error.WriteLine("--row-span and --col-span must be greater than zero.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(options.Horizontal) && string.IsNullOrWhiteSpace(options.Vertical))
            {
                Console.Error.WriteLine("--horizontal or --vertical is required.");
                return 1;
            }

            var result = HwpxTableCellAlignEditor.Apply(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row=" + result.Row.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column=" + result.Column.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row_span=" + result.RowSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column_span=" + result.ColumnSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("horizontal=" + result.Horizontal);
            Console.WriteLine("vertical=" + result.Vertical);
            Console.WriteLine("affected_cells=" + result.AffectedCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("affected_cell_addresses=" + result.AffectedCellAddresses);
            Console.WriteLine("horizontal_paragraphs=" + result.HorizontalParagraphs.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("vertical_cells=" + result.VerticalCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("cloned_para_pr_count=" + result.ClonedParaPrCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableCellDiagonalPackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-cell-diagonal-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index --direction slash|backslash|both|none [--row-span count] [--col-span count] [--width value] [--color #RRGGBB] [--base-border-fill-id id] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  Clones the affected cells' current borderFill definitions, changes diagonal slash/backSlash settings, and retargets affected cells to the clone.");
                return 1;
            }

            var options = new TableCellDiagonalOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1,
                Row = -1,
                Column = -1,
                RowSpan = 1,
                ColumnSpan = 1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--row", StringComparison.OrdinalIgnoreCase))
                {
                    options.Row = ParseIntArgument(RequireValue(args, ref index, "--row"), "row");
                    continue;
                }

                if (string.Equals(args[index], "--column", StringComparison.OrdinalIgnoreCase))
                {
                    options.Column = ParseIntArgument(RequireValue(args, ref index, "--column"), "column");
                    continue;
                }

                if (string.Equals(args[index], "--row-span", StringComparison.OrdinalIgnoreCase))
                {
                    options.RowSpan = ParseIntArgument(RequireValue(args, ref index, "--row-span"), "row-span");
                    continue;
                }

                if (string.Equals(args[index], "--col-span", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--column-span", StringComparison.OrdinalIgnoreCase))
                {
                    var flagName = args[index];
                    options.ColumnSpan = ParseIntArgument(RequireValue(args, ref index, flagName), flagName.TrimStart('-'));
                    continue;
                }

                if (string.Equals(args[index], "--direction", StringComparison.OrdinalIgnoreCase))
                {
                    options.Direction = RequireValue(args, ref index, "--direction");
                    continue;
                }

                if (string.Equals(args[index], "--width", StringComparison.OrdinalIgnoreCase))
                {
                    options.Width = RequireValue(args, ref index, "--width");
                    continue;
                }

                if (string.Equals(args[index], "--color", StringComparison.OrdinalIgnoreCase))
                {
                    options.Color = RequireValue(args, ref index, "--color");
                    continue;
                }

                if (string.Equals(args[index], "--base-border-fill-id", StringComparison.OrdinalIgnoreCase))
                {
                    options.BaseBorderFillId = ParseIntArgument(RequireValue(args, ref index, "--base-border-fill-id"), "base-border-fill-id");
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (options.Row < 0)
            {
                Console.Error.WriteLine("--row is required and must be zero or greater.");
                return 1;
            }

            if (options.Column < 0)
            {
                Console.Error.WriteLine("--column is required and must be zero or greater.");
                return 1;
            }

            if (options.RowSpan <= 0 || options.ColumnSpan <= 0)
            {
                Console.Error.WriteLine("--row-span and --col-span must be greater than zero.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(options.Direction))
            {
                Console.Error.WriteLine("--direction is required.");
                return 1;
            }

            var result = HwpxTableCellDiagonalEditor.Apply(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row=" + result.Row.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column=" + result.Column.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row_span=" + result.RowSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column_span=" + result.ColumnSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("direction=" + result.Direction);
            Console.WriteLine("width=" + result.Width);
            Console.WriteLine("color=" + result.Color);
            Console.WriteLine("base_border_fill_id=" + (result.BaseBorderFillId.HasValue ? result.BaseBorderFillId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
            Console.WriteLine("previous_border_fill_ids=" + result.PreviousBorderFillIds);
            Console.WriteLine("cloned_border_fill_ids=" + result.ClonedBorderFillIds);
            Console.WriteLine("cloned_border_fill_count=" + result.ClonedBorderFillCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("affected_cells=" + result.AffectedCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("affected_cell_addresses=" + result.AffectedCellAddresses);
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableCellBackgroundPackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-cell-background-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index --color #RRGGBB|none [--row-span count] [--col-span count] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  Clones the affected cells' current borderFill definitions, changes solid fill color or clears fillBrush, and retargets affected cells to the clone.");
                return 1;
            }

            var options = new TableCellBackgroundOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1,
                Row = -1,
                Column = -1,
                RowSpan = 1,
                ColumnSpan = 1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--row", StringComparison.OrdinalIgnoreCase))
                {
                    options.Row = ParseIntArgument(RequireValue(args, ref index, "--row"), "row");
                    continue;
                }

                if (string.Equals(args[index], "--column", StringComparison.OrdinalIgnoreCase))
                {
                    options.Column = ParseIntArgument(RequireValue(args, ref index, "--column"), "column");
                    continue;
                }

                if (string.Equals(args[index], "--row-span", StringComparison.OrdinalIgnoreCase))
                {
                    options.RowSpan = ParseIntArgument(RequireValue(args, ref index, "--row-span"), "row-span");
                    continue;
                }

                if (string.Equals(args[index], "--col-span", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--column-span", StringComparison.OrdinalIgnoreCase))
                {
                    var flagName = args[index];
                    options.ColumnSpan = ParseIntArgument(RequireValue(args, ref index, flagName), flagName.TrimStart('-'));
                    continue;
                }

                if (string.Equals(args[index], "--color", StringComparison.OrdinalIgnoreCase))
                {
                    options.Color = RequireValue(args, ref index, "--color");
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (options.Row < 0)
            {
                Console.Error.WriteLine("--row is required and must be zero or greater.");
                return 1;
            }

            if (options.Column < 0)
            {
                Console.Error.WriteLine("--column is required and must be zero or greater.");
                return 1;
            }

            if (options.RowSpan <= 0 || options.ColumnSpan <= 0)
            {
                Console.Error.WriteLine("--row-span and --col-span must be greater than zero.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(options.Color))
            {
                Console.Error.WriteLine("--color is required.");
                return 1;
            }

            var result = HwpxTableCellBackgroundEditor.Apply(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row=" + result.Row.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column=" + result.Column.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("row_span=" + result.RowSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("column_span=" + result.ColumnSpan.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("color=" + result.Color);
            Console.WriteLine("clear_fill=" + BoolText(result.ClearFill));
            Console.WriteLine("previous_border_fill_ids=" + result.PreviousBorderFillIds);
            Console.WriteLine("cloned_border_fill_ids=" + result.ClonedBorderFillIds);
            Console.WriteLine("cloned_border_fill_count=" + result.ClonedBorderFillCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("affected_cells=" + result.AffectedCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("affected_cell_addresses=" + result.AffectedCellAddresses);
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int TableCellSizePackage(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: table-cell-size-package <inputHwpxPath> <outputHwpxPath> --table-index index [--equalize-widths] [--equalize-heights] [--section sectionName] [--report reportMarkdownPath]");
                Console.Error.WriteLine("  Equalizes column widths and/or row heights in a simple top-level table while preserving total table size.");
                return 1;
            }

            var options = new TableCellSizeOptions
            {
                InputPath = args[1],
                OutputPath = args[2],
                Section = "section0",
                TableIndex = -1
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--table-index", StringComparison.OrdinalIgnoreCase))
                {
                    options.TableIndex = ParseIntArgument(RequireValue(args, ref index, "--table-index"), "table-index");
                    continue;
                }

                if (string.Equals(args[index], "--equalize-widths", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--equal-widths", StringComparison.OrdinalIgnoreCase))
                {
                    options.EqualizeWidths = true;
                    continue;
                }

                if (string.Equals(args[index], "--equalize-heights", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--equal-heights", StringComparison.OrdinalIgnoreCase))
                {
                    options.EqualizeHeights = true;
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    options.Section = RequireValue(args, ref index, "--section");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (options.TableIndex < 0)
            {
                Console.Error.WriteLine("--table-index is required and must be zero or greater.");
                return 1;
            }

            if (!options.EqualizeWidths && !options.EqualizeHeights)
            {
                Console.Error.WriteLine("At least one of --equalize-widths or --equalize-heights is required.");
                return 1;
            }

            var result = HwpxTableCellSizeEditor.Apply(options);
            Console.WriteLine(result.OutputPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("table_index=" + result.TableIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("equalize_widths=" + BoolText(result.EqualizeWidths));
            Console.WriteLine("equalize_heights=" + BoolText(result.EqualizeHeights));
            Console.WriteLine("original_rows=" + result.OriginalRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_rows=" + result.NewRows.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_columns=" + result.OriginalColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_columns=" + result.NewColumns.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("physical_cells=" + result.PhysicalCells.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_table_width=" + result.OriginalTableWidth.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_table_width=" + result.NewTableWidth.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_table_height=" + result.OriginalTableHeight.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("new_table_height=" + result.NewTableHeight.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("original_column_widths=" + result.OriginalColumnWidths);
            Console.WriteLine("new_column_widths=" + result.NewColumnWidths);
            Console.WriteLine("original_row_heights=" + result.OriginalRowHeights);
            Console.WriteLine("new_row_heights=" + result.NewRowHeights);
            Console.WriteLine("width_cells_changed=" + result.WidthCellsChanged.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("height_cells_changed=" + result.HeightCellsChanged.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int FillMarkdownTable(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 6)
            {
                Console.Error.WriteLine("Usage: fill-markdown-table <inputPath> <markdownPath> <outputPath> <markdownTableIndex> <hwpTableIndex> [startRow] [startCol] [skipMarkdownRows] [maxRows] [maxCols]");
                return 1;
            }

            var markdownTableIndex = ParseIntArgument(args[4], "markdownTableIndex");
            var hwpTableIndex = ParseIntArgument(args[5], "hwpTableIndex");
            var startRow = args.Length >= 7 ? ParseIntArgument(args[6], "startRow") : 0;
            var startCol = args.Length >= 8 ? ParseIntArgument(args[7], "startCol") : 0;
            var skipMarkdownRows = args.Length >= 9 ? ParseIntArgument(args[8], "skipMarkdownRows") : 0;
            var maxRows = args.Length >= 10 ? ParseIntArgument(args[9], "maxRows") : 0;
            var maxCols = args.Length >= 11 ? ParseIntArgument(args[10], "maxCols") : 0;
            if (markdownTableIndex < 0 || hwpTableIndex < 0 || startRow < 0 || startCol < 0 || skipMarkdownRows < 0 || maxRows < 0 || maxCols < 0)
            {
                Console.Error.WriteLine("table indexes, start positions, skip count, and limits must be zero or greater.");
                return 1;
            }

            var markdown = ReadTextFile(args[2]);
            var tables = MarkdownTableParser.ParseTables(markdown);
            if (markdownTableIndex < 0 || markdownTableIndex >= tables.Count)
            {
                Console.Error.WriteLine("markdownTableIndex is out of range. table_count=" + tables.Count);
                return 1;
            }

            var sourceTable = tables[markdownTableIndex];
            var writtenCells = 0;
            var writtenRows = 0;

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");

                for (var sourceRowIndex = skipMarkdownRows; sourceRowIndex < sourceTable.Count; sourceRowIndex++)
                {
                    if (maxRows > 0 && writtenRows >= maxRows)
                    {
                        break;
                    }

                    var sourceRow = sourceTable[sourceRowIndex];
                    var targetRow = startRow + writtenRows;
                    var writtenCols = 0;

                    for (var sourceColIndex = 0; sourceColIndex < sourceRow.Count; sourceColIndex++)
                    {
                        if (maxCols > 0 && writtenCols >= maxCols)
                        {
                            break;
                        }

                        var targetCol = startCol + writtenCols;
                        var text = sourceRow[sourceColIndex] ?? string.Empty;
                        Console.WriteLine(
                            "[{0},{1}] <- [{2},{3}] {4}",
                            targetRow,
                            targetCol,
                            sourceRowIndex,
                            sourceColIndex,
                            Abbreviate(text, 80));

                        if (!hwp.SetTableCellText(hwpTableIndex, targetRow, targetCol, text))
                        {
                            Console.Error.WriteLine(
                                "Target table cell was not selected. hwpTableIndex={0}, rowMoveCount={1}, columnMoveCount={2}",
                                hwpTableIndex,
                                targetRow,
                                targetCol);
                            return 2;
                        }

                        writtenCells++;
                        writtenCols++;
                    }

                    writtenRows++;
                }

                hwp.SaveAs(args[3], ResolveSaveFormat(args[3]), string.Empty);
            }

            Console.WriteLine(args[3]);
            Console.WriteLine("markdown_table_index=" + markdownTableIndex);
            Console.WriteLine("hwp_table_index=" + hwpTableIndex);
            Console.WriteLine("written_rows=" + writtenRows);
            Console.WriteLine("written_cells=" + writtenCells);
            return 0;
        }

        private static int ExtractFormMap(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: extract-form-map <templateHwpxPath> <outputXmlPath>");
                return 1;
            }

            HwpxFormMap.Extract(args[1], args[2]);
            Console.WriteLine(args[2]);
            return 0;
        }

        private static int FillSubmissionTemplate(string[] args, bool visible, bool keepOpen)
        {
            string profile = "r-and-d-startup-2026";
            string reportPath = null;
            string markdownTableMode = "render";
            string imageMode = "package";
            var assetRoots = new List<string>();
            var values = new List<string>();

            for (var index = 1; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--profile", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --profile.");
                        return 1;
                    }

                    index++;
                    profile = args[index];
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --report.");
                        return 1;
                    }

                    index++;
                    reportPath = args[index];
                    continue;
                }

                if (string.Equals(args[index], "--asset-root", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --asset-root.");
                        return 1;
                    }

                    index++;
                    assetRoots.Add(args[index]);
                    continue;
                }

                if (string.Equals(args[index], "--markdown-table-mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --markdown-table-mode.");
                        return 1;
                    }

                    index++;
                    markdownTableMode = args[index];
                    if (!string.Equals(markdownTableMode, "text", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(markdownTableMode, "render", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine("--markdown-table-mode must be either text or render.");
                        return 1;
                    }

                    continue;
                }

                if (string.Equals(args[index], "--image-mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --image-mode.");
                        return 1;
                    }

                    index++;
                    imageMode = args[index];
                    if (!string.Equals(imageMode, "package", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(imageMode, "com", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(imageMode, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine("--image-mode must be package, com, or none.");
                        return 1;
                    }

                    continue;
                }

                values.Add(args[index]);
            }

            if (values.Count != 3)
            {
                Console.Error.WriteLine("Usage: fill-submission-template <templateHwpxPath> <sourceMarkdownPath> <outputHwpxPath> [--profile r-and-d-startup-2026] [--report reportMarkdownPath] [--asset-root directory] [--markdown-table-mode text|render] [--image-mode package|com|none]");
                return 1;
            }

            if (!string.Equals(profile, "r-and-d-startup-2026", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Unsupported submission profile: " + profile);
                return 1;
            }

            var result = SubmissionTemplateFiller.Fill(values[0], values[1], values[2], assetRoots, markdownTableMode);
            SubmissionTemplateFiller.WriteReport(result, values[0], values[1], values[2], reportPath);
            var imageWritesPassed = ApplySubmissionTemplateImages(result, values[2], visible, keepOpen, imageMode);
            SubmissionTemplateFiller.WriteReport(result, values[0], values[1], values[2], reportPath);
            Console.WriteLine(values[2]);
            Console.WriteLine("profile=" + profile);
            if (result.TemplateCompatibility != null)
            {
                Console.WriteLine("template_compatibility=" + result.TemplateCompatibility.Status);
                Console.WriteLine("template_tables_observed=" + result.TemplateCompatibility.TemplateTableCount);
                Console.WriteLine("template_tables_expected_min=" + result.TemplateCompatibility.ExpectedMinimumTableCount);
            }

            Console.WriteLine("markdown_tables=" + result.MarkdownTables);
            Console.WriteLine("markdown_table_mode=" + result.MarkdownTableMode);
            Console.WriteLine("image_mode=" + imageMode);
            Console.WriteLine("markdown_tables_rendered=" + result.RenderedMarkdownTables);
            Console.WriteLine("markdown_tables_converted_to_text=" + result.MarkdownTablesConvertedToText);
            Console.WriteLine("markdown_images=" + result.MarkdownImages);
            Console.WriteLine("image_writes=" + result.ImageWrites.Count);
            Console.WriteLine("image_writes_applied=" + result.ImageWrites.Count(item => string.Equals(item.Status, "applied", StringComparison.OrdinalIgnoreCase)));
            Console.WriteLine("image_writes_missing_files=" + result.ImageWrites.Count(item => string.IsNullOrWhiteSpace(item.ResolvedPath) || !File.Exists(item.ResolvedPath)));
            Console.WriteLine("cell_writes=" + result.CellWrites);
            Console.WriteLine("paragraph_writes=" + result.ParagraphWrites);
            Console.WriteLine("inserted_paragraphs=" + result.InsertedParagraphs);
            Console.WriteLine("rebuilt_rows=" + result.RebuiltRows);
            Console.WriteLine("rebuilt_table_row_changes=" + result.RebuiltTableRows.Count);
            Console.WriteLine("style_guard_repaired_runs=" + result.StyleRepairedRuns);
            Console.WriteLine("missing_targets=" + result.MissingTargets.Count);
            Console.WriteLine("skipped_unsafe=" + result.SkippedUnsafe.Count);
            Console.WriteLine("skipped_unsupported=" + result.SkippedUnsupported.Count);

            var layoutReportPath = string.IsNullOrWhiteSpace(reportPath)
                ? null
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(reportPath) + ".layout.md");
            var layoutPassed = HwpxLayoutValidator.Validate(values[0], values[2], layoutReportPath, CreateSubmissionProfileLayoutOptions());
            var unmappedImages = SubmissionTemplateFiller.CountUnmappedMarkdownImages(result);
            return result.MissingTargets.Count == 0 &&
                   result.SkippedUnsafe.Count == 0 &&
                   result.SkippedUnsupported.Count == 0 &&
                   unmappedImages == 0 &&
                   imageWritesPassed &&
                   layoutPassed ? 0 : 2;
        }

        private static bool ApplySubmissionTemplateImages(SubmissionTemplateFiller.FillReport result, string outputPath, bool visible, bool keepOpen, string imageMode)
        {
            if (result.ImageWrites.Count == 0)
            {
                return result.MarkdownImages == 0;
            }

            if (string.Equals(imageMode, "none", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var image in result.ImageWrites)
                {
                    image.Status = "failed";
                    image.Note = "image insertion was skipped by --image-mode none";
                }

                return false;
            }

            if (string.Equals(imageMode, "package", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    HwpxPackageImageInserter.InsertImagesAtAnchors(outputPath, result.ImageWrites);
                }
                catch (Exception ex)
                {
                    foreach (var image in result.ImageWrites.Where(item => !string.Equals(item.Status, "applied", StringComparison.OrdinalIgnoreCase)))
                    {
                        image.Status = "failed";
                        image.Note = "package fallback failed: " + ex.GetType().Name + ": " + ex.Message;
                    }
                }

                return result.ImageWrites.All(item => string.Equals(item.Status, "applied", StringComparison.OrdinalIgnoreCase));
            }

            try
            {
                using (var hwp = CreateSession(visible, keepOpen))
                {
                    ConfigureSessionForAutomation(hwp);
                    OpenSessionDocument(hwp, outputPath, string.Empty, "forceopen:true");
                    foreach (var image in result.ImageWrites)
                    {
                        if (string.IsNullOrWhiteSpace(image.ResolvedPath) || !File.Exists(image.ResolvedPath))
                        {
                            image.Status = "failed";
                            image.Note = "image file was not found; searched " + image.CandidatePaths.Count + " candidate path(s)";
                            continue;
                        }

                        if (hwp.InsertPictureAtTextAnchor(image.AnchorText, image.ResolvedPath, image.Width, image.Height, true))
                        {
                            image.Status = "applied";
                            image.Note = "inserted by HWP COM InsertPicture";
                        }
                        else
                        {
                            image.Status = "failed";
                            image.Note = "anchor was not found or InsertPicture failed";
                        }
                    }

                    hwp.SaveAs(outputPath, ResolveSaveFormat(outputPath), string.Empty);
                }
            }
            catch (Exception ex)
            {
                foreach (var image in result.ImageWrites.Where(item => string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase)))
                {
                    image.Status = "failed";
                    image.Note = ex.GetType().Name + ": " + ex.Message;
                }
            }

            if (!result.ImageWrites.All(item => string.Equals(item.Status, "applied", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    HwpxPackageImageInserter.InsertImagesAtAnchors(outputPath, result.ImageWrites);
                }
                catch (Exception ex)
                {
                    foreach (var image in result.ImageWrites.Where(item => !string.Equals(item.Status, "applied", StringComparison.OrdinalIgnoreCase)))
                    {
                        image.Status = "failed";
                        image.Note = image.Note + "; package fallback failed: " + ex.GetType().Name + ": " + ex.Message;
                    }
                }
            }

            return result.ImageWrites.All(item => string.Equals(item.Status, "applied", StringComparison.OrdinalIgnoreCase));
        }

        private static HwpxLayoutValidator.ValidationOptions CreateSubmissionProfileLayoutOptions()
        {
            var options = new HwpxLayoutValidator.ValidationOptions();
            options.AllowedRowGrowthTables.Add(10);
            return options;
        }

        private static int ApplyFormMap(string[] args, bool visible, bool keepOpen)
        {
            var packageMode = false;
            string reportPath = null;
            var values = new List<string>();

            for (var index = 1; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--package", StringComparison.OrdinalIgnoreCase))
                {
                    packageMode = true;
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --report.");
                        return 1;
                    }

                    index++;
                    reportPath = args[index];
                    continue;
                }

                values.Add(args[index]);
            }

            if (values.Count < 3 || values.Count > 4)
            {
                Console.Error.WriteLine("Usage: apply-form-map [--package] <inputHwpxPath> <mapXmlPath> <outputHwpxPath> [maxOperations] [--report reportMarkdownPath]");
                return 1;
            }

            var maxOperations = 0;
            if (values.Count == 4)
            {
                maxOperations = ParseIntArgument(values[3], "maxOperations");
                if (maxOperations < 0)
                {
                    Console.Error.WriteLine("maxOperations must be zero or greater.");
                    return 1;
                }
            }

            HwpxFormMap.ApplyResult result;
            if (packageMode)
            {
                result = HwpxFormMap.ApplyPackage(values[0], values[1], values[2], maxOperations);
                HwpxFormMap.WriteApplyReport(result, values[0], values[1], values[2], "package", reportPath);
                Console.WriteLine(values[2]);
                Console.WriteLine("attempted=" + result.Attempted);
                Console.WriteLine("applied=" + result.Applied);
                Console.WriteLine("failed=" + result.Failed);
                Console.WriteLine("skipped_unsafe=" + result.SkippedUnsafe);
                var layoutReportPath = string.IsNullOrWhiteSpace(reportPath)
                    ? null
                    : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(reportPath) + ".layout.md");
                var layoutPassed = HwpxLayoutValidator.Validate(values[0], values[2], layoutReportPath);
                return result.Failed == 0 && result.SkippedUnsafe == 0 && layoutPassed ? 0 : 2;
            }

            HwpxFormMap.WriteApplyReport(new HwpxFormMap.ApplyResult(), values[0], values[1], values[2], "hwp-com started", reportPath);
            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, values[0], string.Empty, "forceopen:true");
                result = HwpxFormMap.Apply(hwp, values[1], maxOperations);
                hwp.SaveAs(values[2], ResolveSaveFormat(values[2]), string.Empty);
            }

            HwpxFormMap.WriteApplyReport(result, values[0], values[1], values[2], "hwp-com", reportPath);

            Console.WriteLine(values[2]);
            Console.WriteLine("attempted=" + result.Attempted);
            Console.WriteLine("applied=" + result.Applied);
            Console.WriteLine("failed=" + result.Failed);
            Console.WriteLine("skipped_unsafe=" + result.SkippedUnsafe);
            return result.Failed == 0 && result.SkippedUnsafe == 0 ? 0 : 2;
        }

        private static int ProbeFormMap(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: probe-form-map <inputHwpxPath> <mapXmlPath> <reportMarkdownPath> [maxOperations]");
                return 1;
            }

            var maxOperations = 0;
            if (args.Length >= 5)
            {
                maxOperations = ParseIntArgument(args[4], "maxOperations");
                if (maxOperations < 0)
                {
                    Console.Error.WriteLine("maxOperations must be zero or greater.");
                    return 1;
                }
            }

            HwpxFormMap.ProbeResult result;
            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");
                result = HwpxFormMap.Probe(hwp, args[2], args[3], maxOperations);
            }

            Console.WriteLine(args[3]);
            Console.WriteLine("attempted=" + result.Attempted);
            Console.WriteLine("passed=" + result.Passed);
            Console.WriteLine("failed=" + result.Failed);
            Console.WriteLine("skipped=" + result.Skipped);
            return result.Failed == 0 ? 0 : 2;
        }

        private static int ValidateLayout(string[] args)
        {
            var values = new List<string>();
            var options = new HwpxLayoutValidator.ValidationOptions();
            for (var index = 1; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--allow-table-row-change", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--allow-row-growth", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for " + args[index] + ".");
                        return 1;
                    }

                    index++;
                    if (!AddTableIndexes(options.AllowedRowGrowthTables, args[index]))
                    {
                        return 1;
                    }

                    continue;
                }

                if (string.Equals(args[index], "--allow-table-column-change", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(args[index], "--allow-column-change", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for " + args[index] + ".");
                        return 1;
                    }

                    index++;
                    if (!AddTableIndexes(options.AllowedColumnChangeTables, args[index]))
                    {
                        return 1;
                    }

                    continue;
                }

                if (string.Equals(args[index], "--max-leading-style-drift", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --max-leading-style-drift.");
                        return 1;
                    }

                    index++;
                    options.MaxLeadingParagraphStyleDrift = ParseIntArgument(args[index], "max-leading-style-drift");
                    if (options.MaxLeadingParagraphStyleDrift < 0)
                    {
                        Console.Error.WriteLine("--max-leading-style-drift must be zero or greater.");
                        return 1;
                    }

                    continue;
                }

                values.Add(args[index]);
            }

            if (values.Count < 2 || values.Count > 3)
            {
                Console.Error.WriteLine("Usage: validate-layout <templateHwpxPath> <candidateHwpxPath> [reportMarkdownPath] [--allow-table-row-change indexes] [--allow-table-column-change indexes] [--max-leading-style-drift count]");
                return 1;
            }

            var passed = HwpxLayoutValidator.Validate(values[0], values[1], values.Count >= 3 ? values[2] : null, options);
            return passed ? 0 : 2;
        }

        private static int ValidateContent(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: validate-content <candidateHwpxPath> [reportMarkdownPath] [--require text]...");
                return 1;
            }

            string reportPath = null;
            var requiredStrings = new List<string>();
            for (var index = 2; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--require", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --require.");
                        return 1;
                    }

                    index++;
                    requiredStrings.Add(args[index]);
                    continue;
                }

                if (reportPath == null)
                {
                    reportPath = args[index];
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            var passed = HwpxContentValidator.Validate(args[1], reportPath, requiredStrings);
            return passed ? 0 : 2;
        }

        private static int ScanHwpxFeatures(string[] args)
        {
            if (args.Length < 2 || args.Length > 3)
            {
                Console.Error.WriteLine("Usage: scan-hwpx-features <hwpxFileOrDirectory> [reportMarkdownPath]");
                return 1;
            }

            var summary = HwpxFeatureScanner.Scan(args[1]);
            if (args.Length == 3)
            {
                HwpxFeatureScanner.WriteMarkdown(summary, args[2]);
            }

            Console.WriteLine("input=" + summary.InputPath);
            Console.WriteLine("files=" + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("package_errors=" + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("xml_parse_errors=" + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("paragraphs=" + summary.Count("paragraphs").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("tables=" + summary.Count("tables").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("table_cells=" + summary.Count("tableCells").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("merged_cells=" + summary.Count("mergedCells").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("nested_tables=" + summary.Count("nestedTables").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("pictures=" + summary.Count("pictures").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("bindata_entries=" + summary.Count("binDataEntries").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("drawing_shapes=" + summary.Count("drawingShapes").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("fields=" + (summary.Count("fields") + summary.Count("fieldBegins")).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("field_markers=" + summary.Count("fieldMarkers").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("press_fields=" + summary.Count("pressFields").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("form_objects=" + summary.Count("formObjects").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("headers_footers=" + (summary.Count("pageHeaders") + summary.Count("pageFooters") + summary.Count("pageHeaderReferences") + summary.Count("pageFooterReferences")).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("notes=" + (summary.Count("footnotes") + summary.Count("endnotes") + summary.Count("memos") + summary.Count("comments")).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("references=" + (summary.Count("captions") + summary.Count("bookmarks") + summary.Count("crossReferences") + summary.Count("hyperlinks") + summary.Count("tocMarkers") + summary.Count("indexMarkers") + summary.Count("autoNumbers") + summary.Count("pageNumbers")).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("embedded_objects=" + (summary.Count("equations") + summary.Count("charts") + summary.Count("oleObjects") + summary.Count("videos") + summary.Count("sounds")).ToString(CultureInfo.InvariantCulture));
            if (args.Length == 3)
            {
                Console.WriteLine(args[2]);
            }

            return 0;
        }

        private static int ListPictures(string[] args)
        {
            if (args.Length < 2 || args.Length > 3)
            {
                Console.Error.WriteLine("Usage: list-pictures <hwpxFileOrDirectory> [reportMarkdownPath]");
                return 1;
            }

            var summary = HwpxPictureInspector.Inspect(args[1]);
            if (args.Length == 3)
            {
                HwpxPictureInspector.WriteMarkdown(summary, args[2]);
            }

            Console.WriteLine("input=" + summary.InputPath);
            Console.WriteLine("files=" + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("package_errors=" + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("xml_parse_errors=" + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("pictures=" + summary.PictureCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("matched_bindata=" + summary.MatchedBinDataCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("missing_bindata=" + summary.MissingBinDataCount.ToString(CultureInfo.InvariantCulture));
            if (args.Length == 3)
            {
                Console.WriteLine(args[2]);
            }

            return summary.PackageErrors == 0 && summary.XmlParseErrors == 0 ? 0 : 2;
        }

        private static int ReplaceImageControl(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: replace-image-control <inputHwpxPath> <outputHwpxPath> --target control:gso:<index>|picture:<index>|image:<binaryItemIDRef> --image imagePath [--report reportMarkdownPath]");
                return 1;
            }

            var options = new ReplaceImageControlOptions
            {
                InputPath = args[1],
                OutputPath = args[2]
            };

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--target", StringComparison.OrdinalIgnoreCase))
                {
                    options.Target = RequireValue(args, ref index, "--target");
                    continue;
                }

                if (string.Equals(args[index], "--image", StringComparison.OrdinalIgnoreCase))
                {
                    options.ImagePath = RequireValue(args, ref index, "--image");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    options.ReportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unknown replace-image-control option: " + args[index]);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(options.Target) || string.IsNullOrWhiteSpace(options.ImagePath))
            {
                Console.Error.WriteLine("replace-image-control requires --target and --image.");
                return 1;
            }

            var result = HwpxImageControlReplacer.Replace(options);
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                HwpxImageControlReplacer.WriteMarkdown(result, options.ReportPath);
            }

            Console.WriteLine(options.OutputPath);
            Console.WriteLine("verdict=" + result.Verdict);
            if (!string.IsNullOrWhiteSpace(result.FailureReason))
            {
                Console.WriteLine("failure=" + result.FailureReason);
            }

            Console.WriteLine("target=" + result.Target);
            Console.WriteLine("target_part=" + result.TargetPart);
            Console.WriteLine("target_picture_index=" + result.TargetPictureIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("target_package_gso_index=" + result.TargetPackageGsoIndex.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("image_reference=" + result.ImageReference);
            Console.WriteLine("source_sha256=" + result.SourceImage.Sha256);
            Console.WriteLine("before_sha256=" + result.BeforeImage.Sha256);
            Console.WriteLine("after_sha256=" + result.AfterImage.Sha256);
            Console.WriteLine("picture_count_before=" + result.PictureCountBefore.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("picture_count_after=" + result.PictureCountAfter.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("table_count_before=" + result.TableCountBefore.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("table_count_after=" + result.TableCountAfter.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("properties_preserved=" + (result.PropertiesPreserved ? "true" : "false"));
            Console.WriteLine("hash_verified=" + (result.HashVerified ? "true" : "false"));
            Console.WriteLine("layout_passed=" + (result.LayoutPassed.HasValue ? (result.LayoutPassed.Value ? "true" : "false") : "skipped"));
            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                Console.WriteLine(options.ReportPath);
            }

            return result.Success ? 0 : 2;
        }

        private static int ListHeaderFooter(string[] args)
        {
            if (args.Length < 2 || args.Length > 3)
            {
                Console.Error.WriteLine("Usage: list-header-footer <hwpxFileOrDirectory> [reportMarkdownPath]");
                return 1;
            }

            var summary = HwpxFeatureScanner.Scan(args[1]);
            var itemCount = summary.Files.Sum(file => file.HeaderFooterItems.Count);
            var sectionCount = summary.Files
                .SelectMany(file => file.HeaderFooterItems.Select(detail => new { FilePath = file.Path, detail.Section }))
                .Where(item => !string.IsNullOrWhiteSpace(item.Section))
                .Select(item => (item.FilePath ?? string.Empty) + "\u001f" + item.Section)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (args.Length == 3)
            {
                WriteHeaderFooterReport(summary, args[2]);
            }

            Console.WriteLine("input=" + summary.InputPath);
            Console.WriteLine("files=" + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("package_errors=" + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("xml_parse_errors=" + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("header_footer_items=" + itemCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("sections=" + sectionCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("header_bodies=" + summary.Count("pageHeaders").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("footer_bodies=" + summary.Count("pageFooters").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("header_references=" + summary.Count("pageHeaderReferences").ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("footer_references=" + summary.Count("pageFooterReferences").ToString(CultureInfo.InvariantCulture));
            if (args.Length == 3)
            {
                Console.WriteLine(args[2]);
            }

            return 0;
        }

        private static int SetHeaderFooterText(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: set-header-footer-text <inputHwpxPath> <outputHwpxPath> --kind header|footer --anchor text --text replacement [--section sectionName] [--occurrence index] [--report reportMarkdownPath]");
                return 1;
            }

            string kind = null;
            string section = null;
            string anchor = null;
            string text = null;
            string reportPath = null;
            var occurrence = 0;

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--kind", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --kind.");
                        return 1;
                    }

                    kind = args[++index];
                    continue;
                }

                if (string.Equals(args[index], "--section", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --section.");
                        return 1;
                    }

                    section = args[++index];
                    continue;
                }

                if (string.Equals(args[index], "--anchor", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --anchor.");
                        return 1;
                    }

                    anchor = args[++index];
                    continue;
                }

                if (string.Equals(args[index], "--text", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --text.");
                        return 1;
                    }

                    text = args[++index];
                    continue;
                }

                if (string.Equals(args[index], "--occurrence", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out occurrence) || occurrence < 0)
                    {
                        Console.Error.WriteLine("--occurrence requires a zero-based non-negative integer.");
                        return 1;
                    }

                    index++;
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --report.");
                        return 1;
                    }

                    reportPath = args[++index];
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                Console.Error.WriteLine("--kind is required.");
                return 1;
            }

            if (string.IsNullOrEmpty(anchor))
            {
                Console.Error.WriteLine("--anchor is required.");
                return 1;
            }

            if (text == null)
            {
                Console.Error.WriteLine("--text is required.");
                return 1;
            }

            var result = HwpxHeaderFooterTextWriter.Write(args[1], args[2], kind, section, anchor, text, occurrence, reportPath);
            Console.WriteLine("applied=" + BoolText(result.Applied));
            Console.WriteLine("matched_bodies=" + result.MatchedBodies.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("modified_text_runs=" + result.ModifiedTextRuns.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(result.PartPath))
            {
                Console.WriteLine("part=" + result.PartPath);
            }

            Console.WriteLine("note=" + result.Note);
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                Console.WriteLine(reportPath);
            }

            return result.Applied ? 0 : 2;
        }

        private static int PageNumberSet(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: page-number-set <inputPath> <outputPath> [--draw-pos value] [--side-char text] [--report reportMarkdownPath]");
                return 1;
            }

            var drawPos = 5;
            var sideChar = "-";
            string reportPath = null;

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--draw-pos", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out drawPos))
                    {
                        Console.Error.WriteLine("--draw-pos requires an integer value.");
                        return 1;
                    }

                    index++;
                    continue;
                }

                if (string.Equals(args[index], "--side-char", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --side-char.");
                        return 1;
                    }

                    sideChar = args[++index];
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --report.");
                        return 1;
                    }

                    reportPath = args[++index];
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            bool applied;
            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");
                applied = hwp.AddPageNumbering(drawPos, sideChar);
                if (applied)
                {
                    hwp.SaveAs(args[2], ResolveSaveFormat(args[2]), string.Empty);
                }
            }

            WritePageNumberReport(args[1], args[2], drawPos, sideChar, applied, reportPath);
            Console.WriteLine("applied=" + BoolText(applied));
            Console.WriteLine("draw_pos=" + drawPos.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("side_char=" + sideChar);
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                Console.WriteLine(reportPath);
            }

            return applied ? 0 : 2;
        }

        private static int ListControls(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 2 || args.Length > 3)
            {
                Console.Error.WriteLine("Usage: list-controls <inputPath> [reportMarkdownPath]");
                return 1;
            }

            IList<HwpControlInfo> controls;
            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");
                controls = hwp.ListControls();
            }

            Console.WriteLine("controls=" + controls.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var group in controls.GroupBy(item => item.CtrlId).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine("control_type=" + group.Key + ",count=" + group.Count().ToString(CultureInfo.InvariantCulture));
            }

            if (args.Length == 3)
            {
                WriteControlsReport(args[1], controls, args[2]);
                Console.WriteLine(args[2]);
            }

            return 0;
        }

        private static int ProbeCopyFromDoc(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: probe-copy-from-doc <sourcePath> <targetPath> --source all|paragraph-to-end:<text>|table:<index>|image:<index>|control:<ctrlId>:<index> [--target doc-end|anchor:<text>|cell:<table,rowMove,colMove>|control:<ctrlId>:<index>] [--report reportMarkdownPath]");
                return 1;
            }

            string sourceSelectorText = null;
            var targetSelectorText = "doc-end";
            string reportPath = null;

            for (var index = 3; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--source", StringComparison.OrdinalIgnoreCase))
                {
                    sourceSelectorText = RequireValue(args, ref index, "--source");
                    continue;
                }

                if (string.Equals(args[index], "--target", StringComparison.OrdinalIgnoreCase))
                {
                    targetSelectorText = RequireValue(args, ref index, "--target");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    reportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(sourceSelectorText))
            {
                Console.Error.WriteLine("Missing --source.");
                return 1;
            }

            CopyLocation source;
            string reason;
            if (!CopyLocation.TryParse(sourceSelectorText, false, out source, out reason))
            {
                Console.Error.WriteLine(reason);
                return 1;
            }

            CopyLocation target;
            if (!CopyLocation.TryParse(targetSelectorText, true, out target, out reason))
            {
                Console.Error.WriteLine(reason);
                return 1;
            }

            string sourceNote;
            string targetNote;
            bool sourceSelected;
            bool targetSelected;

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");
                sourceSelected = TrySelectCopyLocation(hwp, source, false, out sourceNote);
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[2], string.Empty, "forceopen:true");
                targetSelected = TrySelectCopyLocation(hwp, target, true, out targetNote);
            }

            Console.WriteLine("source_selected=" + BoolText(sourceSelected));
            Console.WriteLine("source_note=" + sourceNote);
            Console.WriteLine("target_selected=" + BoolText(targetSelected));
            Console.WriteLine("target_note=" + targetNote);

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                WriteCopyProbeReport(args[1], args[2], source, target, sourceSelected, sourceNote, targetSelected, targetNote, reportPath);
                Console.WriteLine(reportPath);
            }

            return sourceSelected && targetSelected ? 0 : 2;
        }

        private static int CopyFromDoc(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: copy-from-doc <sourcePath> <targetPath> <outputPath> --source all|paragraph-to-end:<text>|table:<index>|image:<index>|control:<ctrlId>:<index> [--target doc-end|anchor:<text>|cell:<table,rowMove,colMove>|control:<ctrlId>:<index>] [--report reportMarkdownPath]");
                return 1;
            }

            string sourceSelectorText = null;
            var targetSelectorText = "doc-end";
            string reportPath = null;

            for (var index = 4; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--source", StringComparison.OrdinalIgnoreCase))
                {
                    sourceSelectorText = RequireValue(args, ref index, "--source");
                    continue;
                }

                if (string.Equals(args[index], "--target", StringComparison.OrdinalIgnoreCase))
                {
                    targetSelectorText = RequireValue(args, ref index, "--target");
                    continue;
                }

                if (string.Equals(args[index], "--report", StringComparison.OrdinalIgnoreCase))
                {
                    reportPath = RequireValue(args, ref index, "--report");
                    continue;
                }

                Console.Error.WriteLine("Unexpected argument: " + args[index]);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(sourceSelectorText))
            {
                Console.Error.WriteLine("Missing --source.");
                return 1;
            }

            CopyLocation source;
            string reason;
            if (!CopyLocation.TryParse(sourceSelectorText, false, out source, out reason))
            {
                Console.Error.WriteLine(reason);
                return 1;
            }

            CopyLocation target;
            if (!CopyLocation.TryParse(targetSelectorText, true, out target, out reason))
            {
                Console.Error.WriteLine(reason);
                return 1;
            }

            string sourceNote;
            string targetNote;
            string pasteNote;
            bool sourceSelected;
            bool sourceCopied;
            bool targetSelected;
            bool pasted;
            CopyFromDocVerification verification;

            using (var sourceHwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(sourceHwp);
                OpenSessionDocument(sourceHwp, args[1], string.Empty, "forceopen:true");
                sourceSelected = TrySelectCopyLocation(sourceHwp, source, false, out sourceNote);
                sourceCopied = sourceSelected && sourceHwp.CopySelection();
                if (!sourceCopied && sourceSelected)
                {
                    sourceNote = sourceNote + "; Copy failed";
                }

                using (var targetHwp = CreateSession(visible, keepOpen))
                {
                    ConfigureSessionForAutomation(targetHwp);
                    OpenSessionDocument(targetHwp, args[2], string.Empty, "forceopen:true");
                    targetSelected = TrySelectCopyLocation(targetHwp, target, true, out targetNote);
                    pasted = targetSelected && sourceCopied && targetHwp.PasteClipboard();
                    pasteNote = pasted ? "clipboard pasted" : "paste skipped or failed";

                    if (pasted)
                    {
                        targetHwp.SaveAs(args[3], ResolveSaveFormat(args[3]), string.Empty);
                    }
                }
            }

            verification = VerifyCopyFromDocImageReplacement(args[1], args[2], args[3], source, target, pasted);

            Console.WriteLine("source_selected=" + BoolText(sourceSelected));
            Console.WriteLine("source_copied=" + BoolText(sourceCopied));
            Console.WriteLine("source_note=" + sourceNote);
            Console.WriteLine("target_selected=" + BoolText(targetSelected));
            Console.WriteLine("target_note=" + targetNote);
            Console.WriteLine("pasted=" + BoolText(pasted));
            Console.WriteLine("paste_note=" + pasteNote);
            Console.WriteLine("post_verify=" + verification.Verdict);
            if (!string.IsNullOrWhiteSpace(verification.Note))
            {
                Console.WriteLine("post_verify_note=" + verification.Note);
            }

            if (pasted)
            {
                Console.WriteLine(args[3]);
            }

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                WriteCopyFromDocReport(args[1], args[2], args[3], source, target, sourceSelected, sourceCopied, sourceNote, targetSelected, targetNote, pasted, pasteNote, verification, reportPath);
                Console.WriteLine(reportPath);
            }

            return sourceSelected && sourceCopied && targetSelected && pasted && (!verification.Required || verification.Verified) ? 0 : 2;
        }

        private static int DemoList()
        {
            Console.WriteLine("insertText");
            Console.WriteLine("insertPicture");
            Console.WriteLine("insertTable");
            Console.WriteLine("addTableRow");
            Console.WriteLine("addTableColumn");
            Console.WriteLine("deleteTableRow");
            Console.WriteLine("deleteTableColumn");
            Console.WriteLine("selectTableCell");
            Console.WriteLine("selectAllTableCell");
            Console.WriteLine("setBgColorCell");
            Console.WriteLine("setBorderCell");
            Console.WriteLine("resizeWidthTableCell");
            Console.WriteLine("resizeHeightTableCell");
            Console.WriteLine("mergeTableCell");
            Console.WriteLine("splitTableCell");
            Console.WriteLine("deleteCellText");
            Console.WriteLine("pageBreak");
            Console.WriteLine("pageNumbering");
            Console.WriteLine("setCharShape");
            Console.WriteLine("copyShape");
            Console.WriteLine("pasteCopyShape");
            return 0;
        }

        private static int ReplaceAfterMarker(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 5)
            {
                Console.Error.WriteLine("Usage: replace-after-marker <inputPath> <markerText> <contentPath> <outputPath>");
                return 1;
            }

            Console.Error.WriteLine("Warning: replace-after-marker deletes a document region and reinserts plain text. Rich formatting inside the replaced region will not be preserved.");
            var content = ReadTextFile(args[3]);
            if (string.Equals(Path.GetExtension(args[3]), ".md", StringComparison.OrdinalIgnoreCase))
            {
                content = MarkdownTextConverter.ToPlainText(content);
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1]);

                if (!hwp.ReplaceFromMarkerToDocumentEnd(args[2], content))
                {
                    Console.Error.WriteLine("Marker text was not found.");
                    return 2;
                }

                hwp.SaveAs(args[4], string.Empty, string.Empty);
            }

            Console.WriteLine(args[4]);
            return 0;
        }

        private static HwpSession CreateSession(bool visible, bool keepOpen)
        {
            using (var watchdog = ComOperationWatchdog.Start("create HWP session", _comTimeoutMs))
            {
                var hwp = HwpSession.Create(visible, watchdog.Step);
                if (keepOpen)
                {
                    watchdog.Step("Mark HWP session as keep-open");
                    hwp.LeaveOpen();
                }

                watchdog.Step("HWP session ready");
                return hwp;
            }
        }

        private static void ConfigureSessionForAutomation(HwpSession hwp)
        {
            using (var watchdog = ComOperationWatchdog.Start("configure HWP automation", _comTimeoutMs))
            {
                watchdog.Step("Register file path checker module");
                hwp.TryRegisterFilePathCheckerModule();

                watchdog.Step("Set automation message box mode");
                hwp.SetMessageBoxMode(0x10);
            }
        }

        private static void OpenSessionDocument(HwpSession hwp, string inputPath)
        {
            OpenSessionDocument(hwp, inputPath, string.Empty, string.Empty);
        }

        private static void OpenSessionDocument(HwpSession hwp, string inputPath, string format, string options)
        {
            using (var watchdog = ComOperationWatchdog.Start("open HWP document", _comTimeoutMs))
            {
                watchdog.Step("Open document: " + Path.GetFullPath(inputPath));
                hwp.Open(inputPath, format, options);
            }
        }

        private static string ResolveSaveFormat(string path)
        {
            var extension = Path.GetExtension(path ?? string.Empty);
            if (string.Equals(extension, ".hwpx", StringComparison.OrdinalIgnoreCase))
            {
                return "HWPX";
            }

            if (string.Equals(extension, ".hwp", StringComparison.OrdinalIgnoreCase))
            {
                return "HWP";
            }

            if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return "PDF";
            }

            return string.Empty;
        }

        private static HwpSession OpenDocument(string inputPath, bool visible, bool keepOpen)
        {
            var hwp = CreateSession(visible, keepOpen);
            try
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, inputPath);
                return hwp;
            }
            catch
            {
                hwp.Dispose();
                throw;
            }
        }

        private static int RunCommand(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: run <command>");
                return 1;
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                hwp.Run(args[1]);
            }

            Console.WriteLine(args[1]);
            return 0;
        }

        private static int Action(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: action <actionName> [key=value ...] [--open <path>] [--save <path>]");
                return 1;
            }

            var actionName = args[1];
            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            string openPath = null;
            string savePath = null;

            for (var i = 2; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--open", StringComparison.OrdinalIgnoreCase))
                {
                    openPath = RequireValue(args, ref i, "--open");
                    continue;
                }

                if (string.Equals(args[i], "--save", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = RequireValue(args, ref i, "--save");
                    continue;
                }

                var separatorIndex = args[i].IndexOf('=');
                if (separatorIndex <= 0)
                {
                    Console.Error.WriteLine($"Expected key=value pair, got '{args[i]}'.");
                    return 1;
                }

                parameters[args[i].Substring(0, separatorIndex)] = ParseValue(args[i].Substring(separatorIndex + 1));
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);

                if (!string.IsNullOrWhiteSpace(openPath))
                {
                    OpenSessionDocument(hwp, openPath);
                }

                var executed = hwp.ExecuteAction(actionName, set =>
                {
                    foreach (var parameter in parameters)
                    {
                        set.SetItem(parameter.Key, parameter.Value);
                    }
                });

                Console.WriteLine(executed ? "true" : "false");

                if (!string.IsNullOrWhiteSpace(savePath))
                {
                    hwp.SaveAs(savePath, string.Empty, string.Empty);
                    Console.WriteLine(savePath);
                }
            }

            return 0;
        }

        private static string RequireValue(string[] args, ref int index, string optionName)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {optionName}.");
            }

            index++;
            return args[index];
        }

        private static object ParseValue(string raw)
        {
            if (bool.TryParse(raw, out var booleanValue))
            {
                return booleanValue;
            }

            if (int.TryParse(raw, out var intValue))
            {
                return intValue;
            }

            if (double.TryParse(raw, out var doubleValue))
            {
                return doubleValue;
            }

            return raw;
        }

        private static bool ExecuteDemoFeature(HwpSession hwp, string featureName, IDictionary<string, object> parameters)
        {
            switch ((featureName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "inserttext":
                    hwp.InsertText(GetString(parameters, new[] { "text" }, "한글 SDK 텍스트 삽입 기능"));
                    return true;
                case "insertpicture":
                    hwp.InsertPicture(
                        GetRequiredString(parameters, new[] { "path" }),
                        GetBoolean(parameters, new[] { "embedded" }, true),
                        GetInt(parameters, new[] { "sizeoption", "sizeOption" }, 0),
                        GetBoolean(parameters, new[] { "reverse" }, false),
                        GetBoolean(parameters, new[] { "watermark" }, false),
                        GetInt(parameters, new[] { "effect" }, 0),
                        GetInt(parameters, new[] { "width" }, 200),
                        GetInt(parameters, new[] { "height" }, 200));
                    return true;
                case "inserttable":
                    return hwp.InsertTable(
                        GetInt(parameters, new[] { "rows" }, 4),
                        GetInt(parameters, new[] { "cols" }, 4),
                        GetBoolean(parameters, new[] { "treataschar", "treatAsChar" }, true));
                case "addtablerow":
                    return hwp.AddTableRow(
                        GetInt(parameters, new[] { "side" }, 3),
                        GetInt(parameters, new[] { "count" }, 1),
                        GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "addtablecolumn":
                    return hwp.AddTableColumn(
                        GetInt(parameters, new[] { "side" }, 1),
                        GetInt(parameters, new[] { "count" }, 1),
                        GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "deletetablerow":
                    return hwp.DeleteTableRow(GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "deletetablecolumn":
                    return hwp.DeleteTableColumn(GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "selecttablecell":
                    return hwp.SelectTableCell(
                        GetInt(parameters, new[] { "rowmovecount", "rowmoves" }, 0),
                        GetInt(parameters, new[] { "columnmovecount", "columnmoves" }, 0),
                        GetInt(parameters, new[] { "tableindex", "tableIndex" }, 0));
                case "selectalltablecell":
                case "selectalltablecells":
                    return hwp.SelectAllTableCells(GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "setbgcolorcell":
                    return hwp.SetCellBackgroundColor(
                        (byte)GetInt(parameters, new[] { "r", "red" }, 37),
                        (byte)GetInt(parameters, new[] { "g", "green" }, 99),
                        (byte)GetInt(parameters, new[] { "b", "blue" }, 235),
                        GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "setbordercell":
                    return hwp.SetCellBorder(
                        GetInt(parameters, new[] { "width", "borderwidth" }, 12),
                        GetInt(parameters, new[] { "type", "bordertype" }, 11),
                        GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "resizewidthtablecell":
                    return hwp.ResizeTableCellWidth(
                        GetString(parameters, new[] { "mode", "type", "testtype" }, "increase"),
                        GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "resizeheighttablecell":
                    return hwp.ResizeTableCellHeight(
                        GetString(parameters, new[] { "mode", "type", "testtype" }, "increase"),
                        GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "mergetablecell":
                case "mergetablecells":
                    return hwp.MergeTableCells(GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "splittablecell":
                    return hwp.SplitTableCell(
                        GetInt(parameters, new[] { "rows" }, 3),
                        GetInt(parameters, new[] { "cols" }, 3),
                        GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "deletecelltext":
                    return hwp.DeleteCellText(GetInt(parameters, new[] { "tableindex", "tableIndex" }, -1));
                case "pagebreak":
                    return hwp.InsertPageBreak();
                case "pagenumbering":
                    return hwp.AddPageNumbering(
                        GetInt(parameters, new[] { "drawpos", "drawPos" }, 5),
                        GetString(parameters, new[] { "sidechar", "sideChar" }, "-"));
                case "setcharshape":
                    if (HasValue(parameters, "height"))
                    {
                        return hwp.SetCharShapeHeight(
                            GetInt(parameters, new[] { "height" }, 2000),
                            GetBoolean(parameters, new[] { "bold" }, true));
                    }

                    return hwp.SetCharShape(
                        GetInt(parameters, new[] { "size", "fontsize", "fontSize" }, 20),
                        GetBoolean(parameters, new[] { "bold" }, true));
                case "copyshape":
                    return hwp.CopyShape(GetInt(parameters, new[] { "type" }, 0));
                case "pastecopyshape":
                    return hwp.PasteCopiedShape();
                default:
                    throw new ArgumentException("Unsupported demo feature: " + featureName);
            }
        }

        private static int GetInt(IDictionary<string, object> parameters, string key, int defaultValue)
        {
            return GetInt(parameters, new[] { key }, defaultValue);
        }

        private static int GetInt(IDictionary<string, object> parameters, string[] keys, int defaultValue)
        {
            if (!TryGetValue(parameters, keys, out var value))
            {
                return defaultValue;
            }

            return Convert.ToInt32(value);
        }

        private static bool GetBoolean(IDictionary<string, object> parameters, string key, bool defaultValue)
        {
            return GetBoolean(parameters, new[] { key }, defaultValue);
        }

        private static bool GetBoolean(IDictionary<string, object> parameters, string[] keys, bool defaultValue)
        {
            if (!TryGetValue(parameters, keys, out var value))
            {
                return defaultValue;
            }

            return Convert.ToBoolean(value);
        }

        private static string GetString(IDictionary<string, object> parameters, string key, string defaultValue)
        {
            return GetString(parameters, new[] { key }, defaultValue);
        }

        private static string GetString(IDictionary<string, object> parameters, string[] keys, string defaultValue)
        {
            if (!TryGetValue(parameters, keys, out var value))
            {
                return defaultValue;
            }

            return Convert.ToString(value);
        }

        private static string GetRequiredString(IDictionary<string, object> parameters, string key)
        {
            return GetRequiredString(parameters, new[] { key });
        }

        private static string GetRequiredString(IDictionary<string, object> parameters, string[] keys)
        {
            var value = GetString(parameters, keys, null);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Missing required parameter: " + string.Join(" | ", keys));
            }

            return value;
        }

        private static bool HasValue(IDictionary<string, object> parameters, string key)
        {
            return TryGetValue(parameters, new[] { key }, out _);
        }

        private static bool TryGetValue(IDictionary<string, object> parameters, string[] keys, out object value)
        {
            foreach (var key in keys)
            {
                if (parameters.TryGetValue(key, out value) && value != null)
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static string ReadTextFile(string path)
        {
            try
            {
                return File.ReadAllText(path, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException)
            {
                return File.ReadAllText(path, Encoding.Default);
            }
        }

        private static int ParseIntArgument(string raw, string name)
        {
            int value;
            if (!int.TryParse(raw, out value))
            {
                throw new ArgumentException(name + " must be an integer.");
            }

            return value;
        }

        private static bool AddTableIndexes(ISet<int> indexes, string raw)
        {
            foreach (var token in (raw ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim();
                int value;
                if (!int.TryParse(trimmed, out value) || value < 0)
                {
                    Console.Error.WriteLine("table index must be a zero or positive integer: " + trimmed);
                    return false;
                }

                indexes.Add(value);
            }

            return true;
        }

        private static int WriteTextResult(string text, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                Console.WriteLine(text);
                return 0;
            }

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, text ?? string.Empty, new UTF8Encoding(true));
            Console.WriteLine(outputPath);
            return 0;
        }

        private static string Abbreviate(string value, int maxLength = 40)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private static void WriteHeaderFooterReport(HwpxFeatureScanner.ScanSummary summary, string reportPath)
        {
            var rows = summary.Files
                .SelectMany(file => file.HeaderFooterItems.Select(detail => new { File = file, Detail = detail }))
                .ToList();
            var report = new StringBuilder();
            report.AppendLine("# HWPX Header/Footer Inventory");
            report.AppendLine();
            report.AppendLine("- input: `" + summary.InputPath + "`");
            report.AppendLine("- files: " + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- package errors: " + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- XML parse errors: " + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- items: " + rows.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine();
            report.AppendLine("## Summary");
            report.AppendLine();
            report.AppendLine("| kind | body | reference |");
            report.AppendLine("| --- | ---: | ---: |");
            report.AppendLine("| header | " + summary.Count("pageHeaders").ToString(CultureInfo.InvariantCulture) + " | " + summary.Count("pageHeaderReferences").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| footer | " + summary.Count("pageFooters").ToString(CultureInfo.InvariantCulture) + " | " + summary.Count("pageFooterReferences").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine();

            var bySection = rows
                .GroupBy(row => (row.File.Path ?? string.Empty) + "\u001f" + (string.IsNullOrWhiteSpace(row.Detail.Section) ? "(package part)" : row.Detail.Section), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.First().File.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => string.IsNullOrWhiteSpace(group.First().Detail.Section) ? "(package part)" : group.First().Detail.Section, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (bySection.Count > 0)
            {
                report.AppendLine("## Sections");
                report.AppendLine();
                report.AppendLine("| file | section | headers | footers | references | bodies |");
                report.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
                foreach (var group in bySection)
                {
                    var first = group.First();
                    var section = string.IsNullOrWhiteSpace(first.Detail.Section) ? "(package part)" : first.Detail.Section;
                    report.Append("| ");
                    report.Append(EscapeMarkdownTable(Path.GetFileName(first.File.Path)));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(section));
                    report.Append(" | ");
                    report.Append(group.Count(row => string.Equals(row.Detail.Kind, "header", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture));
                    report.Append(" | ");
                    report.Append(group.Count(row => string.Equals(row.Detail.Kind, "footer", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture));
                    report.Append(" | ");
                    report.Append(group.Count(row => string.Equals(row.Detail.Role, "reference", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture));
                    report.Append(" | ");
                    report.Append(group.Count(row => string.Equals(row.Detail.Role, "body", StringComparison.OrdinalIgnoreCase)).ToString(CultureInfo.InvariantCulture));
                    report.AppendLine(" |");
                }

                report.AppendLine();
            }

            report.AppendLine("## Items");
            report.AppendLine();
            if (rows.Count == 0)
            {
                report.AppendLine("- No header/footer items detected.");
            }
            else
            {
                report.AppendLine("| file | section | part | kind | role | applyPageType | ref | paragraphs | tables | pictures | shapes | text |");
                report.AppendLine("| --- | --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- |");
                foreach (var row in rows.OrderBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Detail.PartPath, StringComparer.Ordinal).ThenBy(item => item.Detail.Kind, StringComparer.Ordinal).ThenBy(item => item.Detail.Role, StringComparer.Ordinal))
                {
                    report.Append("| ");
                    report.Append(EscapeMarkdownTable(Path.GetFileName(row.File.Path)));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.Section));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.PartPath));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.Kind));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.Role));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.ApplyPageType));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.Reference));
                    report.Append(" | ");
                    report.Append(row.Detail.Paragraphs.ToString(CultureInfo.InvariantCulture));
                    report.Append(" | ");
                    report.Append(row.Detail.Tables.ToString(CultureInfo.InvariantCulture));
                    report.Append(" | ");
                    report.Append(row.Detail.Pictures.ToString(CultureInfo.InvariantCulture));
                    report.Append(" | ");
                    report.Append(row.Detail.Shapes.ToString(CultureInfo.InvariantCulture));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(Abbreviate(row.Detail.Text, 100)));
                    report.AppendLine(" |");
                }
            }

            WriteUtf8File(reportPath, report.ToString());
        }

        private static void WriteFieldInventoryReport(
            HwpxFeatureScanner.ScanSummary summary,
            bool includeCom,
            string comRaw,
            IList<ComFieldInventoryItem> comFields,
            int option,
            int optionEx,
            string reportPath)
        {
            var rows = summary.Files
                .SelectMany(file => file.FieldFormItems.Select(detail => new { File = file, Detail = detail }))
                .ToList();
            var report = new StringBuilder();
            report.AppendLine("# HWPX Field/Form Inventory");
            report.AppendLine();
            report.AppendLine("- input: `" + summary.InputPath + "`");
            report.AppendLine("- files: " + summary.Files.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- package errors: " + summary.PackageErrors.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- XML parse errors: " + summary.XmlParseErrors.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- package field/form items: " + rows.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- COM enabled: " + BoolText(includeCom));
            report.AppendLine();
            report.AppendLine("## Summary");
            report.AppendLine();
            report.AppendLine("| signal | count |");
            report.AppendLine("| --- | ---: |");
            report.AppendLine("| field markers | " + summary.Count("fieldMarkers").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| fields | " + summary.Count("fields").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| field begins | " + summary.Count("fieldBegins").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| field ends | " + summary.Count("fieldEnds").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| press fields | " + summary.Count("pressFields").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| form objects | " + summary.Count("formObjects").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| check boxes | " + summary.Count("checkBoxes").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| radio buttons | " + summary.Count("radioButtons").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| combo boxes | " + summary.Count("comboBoxes").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| edit fields | " + summary.Count("editFields").ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine();

            report.AppendLine("## Package Items");
            report.AppendLine();
            if (rows.Count == 0)
            {
                report.AppendLine("- No package field/form items detected.");
            }
            else
            {
                report.AppendLine("| file | part | kind | attrs | text |");
                report.AppendLine("| --- | --- | --- | --- | --- |");
                foreach (var row in rows.OrderBy(item => item.File.Path, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Detail.PartPath, StringComparer.Ordinal).ThenBy(item => item.Detail.Kind, StringComparer.Ordinal).ThenBy(item => item.Detail.Attributes, StringComparer.Ordinal))
                {
                    report.Append("| ");
                    report.Append(EscapeMarkdownTable(Path.GetFileName(row.File.Path)));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.PartPath));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.Kind));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(row.Detail.Attributes));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(Abbreviate(row.Detail.Text, 100)));
                    report.AppendLine(" |");
                }
            }

            report.AppendLine();
            report.AppendLine("## COM Fields");
            report.AppendLine();
            if (!includeCom)
            {
                report.AppendLine("- COM field listing was not requested.");
            }
            else
            {
                report.AppendLine("- option: " + option.ToString(CultureInfo.InvariantCulture));
                report.AppendLine("- optionEx: " + optionEx.ToString(CultureInfo.InvariantCulture));
                report.AppendLine("- raw length: " + (comRaw ?? string.Empty).Length.ToString(CultureInfo.InvariantCulture));
                report.AppendLine();
                if (comFields.Count == 0)
                {
                    report.AppendLine("- No COM field names parsed.");
                }
                else
                {
                    report.AppendLine("| name | exists | text | error |");
                    report.AppendLine("| --- | --- | --- | --- |");
                    foreach (var field in comFields.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        report.Append("| ");
                        report.Append(EscapeMarkdownTable(field.Name));
                        report.Append(" | ");
                        report.Append(BoolText(field.Exists));
                        report.Append(" | ");
                        report.Append(EscapeMarkdownTable(Abbreviate(field.Text, 100)));
                        report.Append(" | ");
                        report.Append(EscapeMarkdownTable(field.Error));
                        report.AppendLine(" |");
                    }
                }

                if (!string.IsNullOrEmpty(comRaw))
                {
                    report.AppendLine();
                    report.AppendLine("```text");
                    report.AppendLine(comRaw.Replace("\0", "\\0"));
                    report.AppendLine("```");
                }
            }

            WriteUtf8File(reportPath, report.ToString());
        }

        private static void WritePageNumberReport(string inputPath, string outputPath, int drawPos, string sideChar, bool applied, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# HWP Page Number Set");
            report.AppendLine();
            report.AppendLine("- input: `" + inputPath + "`");
            report.AppendLine("- output: `" + outputPath + "`");
            report.AppendLine("- verdict: " + (applied ? "applied" : "failed"));
            report.AppendLine();
            report.AppendLine("| field | value |");
            report.AppendLine("| --- | --- |");
            report.AppendLine("| drawPos | " + drawPos.ToString(CultureInfo.InvariantCulture) + " |");
            report.AppendLine("| sideChar | " + EscapeMarkdownTable(sideChar) + " |");
            report.AppendLine("| applied | " + BoolText(applied) + " |");

            WriteUtf8File(reportPath, report.ToString());
        }

        private static void WriteControlsReport(string inputPath, IList<HwpControlInfo> controls, string reportPath)
        {
            var report = new StringBuilder();
            report.AppendLine("# HWP Control Inventory");
            report.AppendLine();
            report.AppendLine("- input: `" + inputPath + "`");
            report.AppendLine("- controls: " + controls.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine();
            report.AppendLine("## Counts");
            report.AppendLine();
            report.AppendLine("| ctrlId | count |");
            report.AppendLine("| --- | ---: |");
            foreach (var group in controls.GroupBy(item => item.CtrlId).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                report.Append("| ");
                report.Append(EscapeMarkdownTable(group.Key));
                report.Append(" | ");
                report.Append(group.Count().ToString(CultureInfo.InvariantCulture));
                report.AppendLine(" |");
            }

            report.AppendLine();
            report.AppendLine("## Controls");
            report.AppendLine();
            report.AppendLine("| index | ctrlId | typeIndex |");
            report.AppendLine("| ---: | --- | ---: |");
            foreach (var control in controls)
            {
                report.Append("| ");
                report.Append(control.Index.ToString(CultureInfo.InvariantCulture));
                report.Append(" | ");
                report.Append(EscapeMarkdownTable(control.CtrlId));
                report.Append(" | ");
                report.Append(control.TypeIndex.ToString(CultureInfo.InvariantCulture));
                report.AppendLine(" |");
            }

            WriteUtf8File(reportPath, report.ToString());
        }

        private static bool TrySelectCopyLocation(HwpSession hwp, CopyLocation location, bool target, out string note)
        {
            if (location.Kind == "doc-end")
            {
                var moved = hwp.MoveDocEnd();
                note = moved ? "moved to document end" : "MoveDocEnd failed";
                return moved;
            }

            if (location.Kind == "all")
            {
                if (target)
                {
                    note = "all selector is source-only";
                    return false;
                }

                var selected = hwp.SelectAll();
                note = selected ? "selected entire source document" : "SelectAll failed";
                return selected;
            }

            if (location.Kind == "paragraph-to-end")
            {
                if (target)
                {
                    note = "paragraph-to-end selector is source-only";
                    return false;
                }

                var selected = hwp.SelectParagraphFromTextAnchorToDocumentEnd(location.Text, location.Index);
                note = selected
                    ? "selected from paragraph containing anchor occurrence " + location.Index.ToString(CultureInfo.InvariantCulture) + " to document end"
                    : "paragraph-to-end source anchor was not found";
                return selected;
            }

            if (location.Kind == "table")
            {
                var selected = hwp.SelectTableControl(location.Index);
                note = selected
                    ? "selected table control " + location.Index.ToString(CultureInfo.InvariantCulture)
                    : "table control was not found: " + location.Index.ToString(CultureInfo.InvariantCulture);
                return selected;
            }

            if (location.Kind == "image")
            {
                var selected = hwp.SelectControl("gso", location.Index);
                note = selected
                    ? "selected image/gso control " + location.Index.ToString(CultureInfo.InvariantCulture)
                    : "image/gso control was not found: " + location.Index.ToString(CultureInfo.InvariantCulture);
                return selected;
            }

            if (location.Kind == "control")
            {
                var selected = hwp.SelectControl(location.CtrlId, location.Index);
                note = selected
                    ? "selected control " + location.CtrlId + ":" + location.Index.ToString(CultureInfo.InvariantCulture)
                    : "control was not found: " + location.CtrlId + ":" + location.Index.ToString(CultureInfo.InvariantCulture);
                return selected;
            }

            if (location.Kind == "anchor")
            {
                var moved = hwp.MoveToTextAnchor(location.Text, location.Index);
                note = moved
                    ? "moved to anchor occurrence " + location.Index.ToString(CultureInfo.InvariantCulture)
                    : "anchor was not found";
                return moved;
            }

            if (location.Kind == "cell")
            {
                if (!target)
                {
                    note = "cell selector is target-only";
                    return false;
                }

                var selected = hwp.SelectTableCell(location.Row, location.Column, location.TableIndex);
                note = selected
                    ? "selected target cell"
                    : "target cell was not selected";
                return selected;
            }

            note = "unsupported selector: " + location.Raw;
            return false;
        }

        private static CopyFromDocVerification VerifyCopyFromDocImageReplacement(
            string sourcePath,
            string targetPath,
            string outputPath,
            CopyLocation source,
            CopyLocation target,
            bool pasted)
        {
            var result = new CopyFromDocVerification
            {
                Verdict = "skipped",
                SourceSelector = source == null ? string.Empty : source.Raw,
                TargetSelector = target == null ? string.Empty : target.Raw
            };

            if (!pasted)
            {
                result.Note = "paste was not applied";
                return result;
            }

            if (!IsGsoImageSelector(source) || !IsGsoControlTarget(target))
            {
                result.Note = "post-verify currently applies only to image/control:gso source copied onto control:gso target";
                return result;
            }

            result.Required = true;
            if (!IsHwpxPath(sourcePath) || !IsHwpxPath(targetPath) || !IsHwpxPath(outputPath))
            {
                result.Verdict = "failed";
                result.Note = "post-verify requires HWPX source, target, and output paths";
                return result;
            }

            try
            {
                var sourceInventory = HwpxPictureInspector.Inspect(sourcePath);
                var targetBeforeInventory = HwpxPictureInspector.Inspect(targetPath);
                var outputInventory = HwpxPictureInspector.Inspect(outputPath);
                var sourcePicture = FindPictureByGsoIndex(sourceInventory, source.Index);
                var beforePicture = FindPictureByGsoIndex(targetBeforeInventory, target.Index);
                var afterPicture = FindPictureByGsoIndex(outputInventory, target.Index);

                result.SourcePictureCount = sourceInventory.PictureCount;
                result.TargetPictureCountBefore = targetBeforeInventory.PictureCount;
                result.TargetPictureCountAfter = outputInventory.PictureCount;
                result.SourceBinData = sourcePicture == null ? string.Empty : sourcePicture.BinDataPath;
                result.TargetBeforeBinData = beforePicture == null ? string.Empty : beforePicture.BinDataPath;
                result.TargetAfterBinData = afterPicture == null ? string.Empty : afterPicture.BinDataPath;
                result.SourceSha256 = sourcePicture == null ? string.Empty : sourcePicture.Sha256;
                result.TargetBeforeSha256 = beforePicture == null ? string.Empty : beforePicture.Sha256;
                result.TargetAfterSha256 = afterPicture == null ? string.Empty : afterPicture.Sha256;
                result.SourcePixels = sourcePicture == null ? string.Empty : sourcePicture.PixelSize;
                result.TargetBeforePixels = beforePicture == null ? string.Empty : beforePicture.PixelSize;
                result.TargetAfterPixels = afterPicture == null ? string.Empty : afterPicture.PixelSize;

                if (sourcePicture == null)
                {
                    result.Verdict = "failed";
                    result.Note = "source image/gso picture was not found in HWPX package inventory";
                    return result;
                }

                if (beforePicture == null)
                {
                    result.Verdict = "failed";
                    result.Note = "target before picture was not found in HWPX package inventory";
                    return result;
                }

                if (afterPicture == null)
                {
                    result.Verdict = "failed";
                    result.Note = "target after picture was not found in HWPX package inventory";
                    return result;
                }

                result.HashMatched = !string.IsNullOrWhiteSpace(result.SourceSha256) &&
                    string.Equals(result.SourceSha256, result.TargetAfterSha256, StringComparison.OrdinalIgnoreCase);
                result.PictureCountPreserved = result.TargetPictureCountBefore == result.TargetPictureCountAfter;
                result.TargetChanged = !string.Equals(result.TargetBeforeSha256, result.TargetAfterSha256, StringComparison.OrdinalIgnoreCase);
                result.Verified = result.HashMatched && result.PictureCountPreserved;
                result.Verdict = result.Verified ? "verified" : "failed";
                if (!result.Verified)
                {
                    result.Note = "source image hash did not match target after hash or target picture count changed";
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Verdict = "failed";
                result.Note = "post-verify failed: " + ex.GetType().Name + ": " + ex.Message;
                return result;
            }
        }

        private static bool IsGsoImageSelector(CopyLocation source)
        {
            if (source == null)
            {
                return false;
            }

            return string.Equals(source.Kind, "image", StringComparison.OrdinalIgnoreCase) ||
                   (string.Equals(source.Kind, "control", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(source.CtrlId, "gso", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsGsoControlTarget(CopyLocation target)
        {
            return target != null &&
                   string.Equals(target.Kind, "control", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(target.CtrlId, "gso", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHwpxPath(string path)
        {
            return string.Equals(Path.GetExtension(path), ".hwpx", StringComparison.OrdinalIgnoreCase);
        }

        private static PictureInventoryItem FindPictureByGsoIndex(PictureInventorySummary summary, int gsoIndex)
        {
            return summary == null
                ? null
                : summary.Files
                    .SelectMany(file => file.Pictures)
                    .FirstOrDefault(picture => picture.GsoTypeIndex == gsoIndex);
        }

        private static void WriteCopyProbeReport(
            string sourcePath,
            string targetPath,
            CopyLocation source,
            CopyLocation target,
            bool sourceSelected,
            string sourceNote,
            bool targetSelected,
            string targetNote,
            string reportPath)
        {
            var report = new StringBuilder();
            report.AppendLine("# Copy From Doc Probe");
            report.AppendLine();
            report.AppendLine("- source: `" + sourcePath + "`");
            report.AppendLine("- target: `" + targetPath + "`");
            report.AppendLine("- source selector: `" + source.Raw + "`");
            report.AppendLine("- target selector: `" + target.Raw + "`");
            report.AppendLine("- verdict: " + (sourceSelected && targetSelected ? "ready" : "blocked"));
            report.AppendLine();
            report.AppendLine("| side | selected | note |");
            report.AppendLine("| --- | --- | --- |");
            report.Append("| source | ");
            report.Append(BoolText(sourceSelected));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(sourceNote));
            report.AppendLine(" |");
            report.Append("| target | ");
            report.Append(BoolText(targetSelected));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(targetNote));
            report.AppendLine(" |");

            WriteUtf8File(reportPath, report.ToString());
        }

        private static void WriteCopyFromDocReport(
            string sourcePath,
            string targetPath,
            string outputPath,
            CopyLocation source,
            CopyLocation target,
            bool sourceSelected,
            bool sourceCopied,
            string sourceNote,
            bool targetSelected,
            string targetNote,
            bool pasted,
            string pasteNote,
            CopyFromDocVerification verification,
            string reportPath)
        {
            verification = verification ?? new CopyFromDocVerification { Verdict = "skipped", Note = "not evaluated" };
            var comApplied = sourceSelected && sourceCopied && targetSelected && pasted;
            var verdict = comApplied ? "applied" : "failed";
            if (comApplied && verification.Required && !verification.Verified)
            {
                verdict = "failed-post-verify";
            }

            var report = new StringBuilder();
            report.AppendLine("# Copy From Doc Report");
            report.AppendLine();
            report.AppendLine("- source: `" + sourcePath + "`");
            report.AppendLine("- target: `" + targetPath + "`");
            report.AppendLine("- output: `" + outputPath + "`");
            report.AppendLine("- source selector: `" + source.Raw + "`");
            report.AppendLine("- target selector: `" + target.Raw + "`");
            report.AppendLine("- verdict: " + verdict);
            report.AppendLine();
            report.AppendLine("| step | ok | note |");
            report.AppendLine("| --- | --- | --- |");
            AppendCopyReportRow(report, "source select", sourceSelected, sourceNote);
            AppendCopyReportRow(report, "source copy", sourceCopied, sourceCopied ? "selection copied" : "copy failed");
            AppendCopyReportRow(report, "target select", targetSelected, targetNote);
            AppendCopyReportRow(report, "paste", pasted, pasteNote);

            report.AppendLine();
            report.AppendLine("## Post Verify");
            report.AppendLine();
            report.AppendLine("- verdict: " + verification.Verdict);
            report.AppendLine("- required: " + BoolText(verification.Required));
            report.AppendLine("- verified: " + BoolText(verification.Verified));
            if (!string.IsNullOrWhiteSpace(verification.Note))
            {
                report.AppendLine("- note: " + verification.Note);
            }

            report.AppendLine("- source selector: `" + verification.SourceSelector + "`");
            report.AppendLine("- target selector: `" + verification.TargetSelector + "`");
            report.AppendLine("- source picture count: " + verification.SourcePictureCount.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- target picture count: " + verification.TargetPictureCountBefore.ToString(CultureInfo.InvariantCulture) + " -> " + verification.TargetPictureCountAfter.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- hash matched: " + BoolText(verification.HashMatched));
            report.AppendLine("- target changed: " + BoolText(verification.TargetChanged));
            report.AppendLine("- picture count preserved: " + BoolText(verification.PictureCountPreserved));
            report.AppendLine();
            report.AppendLine("| side | BinData | pixels | sha256 |");
            report.AppendLine("| --- | --- | --- | --- |");
            AppendCopyVerifyImageRow(report, "source", verification.SourceBinData, verification.SourcePixels, verification.SourceSha256);
            AppendCopyVerifyImageRow(report, "target before", verification.TargetBeforeBinData, verification.TargetBeforePixels, verification.TargetBeforeSha256);
            AppendCopyVerifyImageRow(report, "target after", verification.TargetAfterBinData, verification.TargetAfterPixels, verification.TargetAfterSha256);

            WriteUtf8File(reportPath, report.ToString());
        }

        private static void AppendCopyVerifyImageRow(StringBuilder report, string side, string binData, string pixels, string sha256)
        {
            report.Append("| ");
            report.Append(EscapeMarkdownTable(side));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(binData));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(pixels));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(sha256));
            report.AppendLine(" |");
        }

        private static void AppendCopyReportRow(StringBuilder report, string step, bool ok, string note)
        {
            report.Append("| ");
            report.Append(EscapeMarkdownTable(step));
            report.Append(" | ");
            report.Append(BoolText(ok));
            report.Append(" | ");
            report.Append(EscapeMarkdownTable(note));
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

        private static IList<string> SplitComFieldNames(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw
                .Split(new[] { '\u0002', '\u0003', '\0', '\r', '\n', '\t', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void WriteUtf8File(string path, string text)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, text ?? string.Empty, new UTF8Encoding(true));
        }

        private static string BoolText(bool value)
        {
            return value.ToString().ToLowerInvariant();
        }

        private sealed class CopyLocation
        {
            public string Raw { get; private set; }

            public string Kind { get; private set; }

            public string CtrlId { get; private set; }

            public int Index { get; private set; }

            public string Text { get; private set; }

            public int TableIndex { get; private set; }

            public int Row { get; private set; }

            public int Column { get; private set; }

            public static bool TryParse(string raw, bool target, out CopyLocation location, out string reason)
            {
                location = null;
                reason = null;

                if (string.IsNullOrWhiteSpace(raw))
                {
                    reason = "Selector is required.";
                    return false;
                }

                var trimmed = raw.Trim();
                if (target && string.Equals(trimmed, "doc-end", StringComparison.OrdinalIgnoreCase))
                {
                    location = new CopyLocation { Raw = trimmed, Kind = "doc-end" };
                    return true;
                }

                if (!target && string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
                {
                    location = new CopyLocation { Raw = trimmed, Kind = "all" };
                    return true;
                }

                if (target && string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "all selector is source-only.";
                    return false;
                }

                if (trimmed.StartsWith("paragraph-to-end:", StringComparison.OrdinalIgnoreCase))
                {
                    if (target)
                    {
                        reason = "paragraph-to-end selector is source-only.";
                        return false;
                    }

                    var text = trimmed.Substring("paragraph-to-end:".Length);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        reason = "paragraph-to-end selector requires text.";
                        return false;
                    }

                    location = new CopyLocation { Raw = trimmed, Kind = "paragraph-to-end", Text = text, Index = 0 };
                    return true;
                }

                if (trimmed.StartsWith("table:", StringComparison.OrdinalIgnoreCase))
                {
                    if (target)
                    {
                        reason = "table selector is source-only; use control:tbl:<index> as an explicit target control selector.";
                        return false;
                    }

                    int index;
                    if (!TryParseNonNegative(trimmed.Substring("table:".Length), out index))
                    {
                        reason = "table selector must be table:<zero-based-index>.";
                        return false;
                    }

                    location = new CopyLocation { Raw = trimmed, Kind = "table", Index = index };
                    return true;
                }

                if (trimmed.StartsWith("image:", StringComparison.OrdinalIgnoreCase))
                {
                    if (target)
                    {
                        reason = "image selector is source-only; use control:gso:<index> as an explicit target control selector.";
                        return false;
                    }

                    int index;
                    if (!TryParseNonNegative(trimmed.Substring("image:".Length), out index))
                    {
                        reason = "image selector must be image:<zero-based-index>.";
                        return false;
                    }

                    location = new CopyLocation { Raw = trimmed, Kind = "image", Index = index };
                    return true;
                }

                if (trimmed.StartsWith("control:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed.Substring("control:".Length);
                    var separator = value.LastIndexOf(':');
                    string ctrlId;
                    var typeIndex = 0;

                    if (separator >= 0)
                    {
                        ctrlId = value.Substring(0, separator);
                        if (!TryParseNonNegative(value.Substring(separator + 1), out typeIndex))
                        {
                            reason = "control selector must be control:<ctrlId>:<zero-based-type-index>.";
                            return false;
                        }
                    }
                    else
                    {
                        ctrlId = value;
                    }

                    if (string.IsNullOrWhiteSpace(ctrlId))
                    {
                        reason = "control selector requires ctrlId.";
                        return false;
                    }

                    location = new CopyLocation { Raw = trimmed, Kind = "control", CtrlId = ctrlId, Index = typeIndex };
                    return true;
                }

                if (trimmed.StartsWith("anchor:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!target)
                    {
                        reason = "anchor selector is target-only; use paragraph-to-end:<text> for source paragraph blocks.";
                        return false;
                    }

                    var text = trimmed.Substring("anchor:".Length);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        reason = "anchor selector requires text.";
                        return false;
                    }

                    location = new CopyLocation { Raw = trimmed, Kind = "anchor", Text = text, Index = 0 };
                    return true;
                }

                if (target && trimmed.StartsWith("cell:", StringComparison.OrdinalIgnoreCase))
                {
                    var tokens = trimmed.Substring("cell:".Length).Split(',');
                    if (tokens.Length != 3)
                    {
                        reason = "cell selector must be cell:<tableIndex>,<rowMoveCount>,<columnMoveCount>.";
                        return false;
                    }

                    int tableIndex;
                    int row;
                    int column;
                    if (!TryParseNonNegative(tokens[0], out tableIndex) ||
                        !TryParseNonNegative(tokens[1], out row) ||
                        !TryParseNonNegative(tokens[2], out column))
                    {
                        reason = "cell selector values must be zero-based non-negative integers.";
                        return false;
                    }

                    location = new CopyLocation { Raw = trimmed, Kind = "cell", TableIndex = tableIndex, Row = row, Column = column };
                    return true;
                }

                reason = "Unsupported selector: " + raw;
                return false;
            }

            private static bool TryParseNonNegative(string raw, out int value)
            {
                return int.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
            }
        }

        private sealed class CopyFromDocVerification
        {
            public string Verdict { get; set; }

            public bool Required { get; set; }

            public bool Verified { get; set; }

            public string Note { get; set; }

            public string SourceSelector { get; set; }

            public string TargetSelector { get; set; }

            public int SourcePictureCount { get; set; }

            public int TargetPictureCountBefore { get; set; }

            public int TargetPictureCountAfter { get; set; }

            public string SourceBinData { get; set; }

            public string TargetBeforeBinData { get; set; }

            public string TargetAfterBinData { get; set; }

            public string SourcePixels { get; set; }

            public string TargetBeforePixels { get; set; }

            public string TargetAfterPixels { get; set; }

            public string SourceSha256 { get; set; }

            public string TargetBeforeSha256 { get; set; }

            public string TargetAfterSha256 { get; set; }

            public bool HashMatched { get; set; }

            public bool TargetChanged { get; set; }

            public bool PictureCountPreserved { get; set; }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("OpenHwp.Automation.Cli");
            Console.WriteLine("  [--visible] [--keep-open] [--com-timeout-ms <ms>|--no-com-timeout] version");
            Console.WriteLine("  [--visible] [--keep-open] [--com-timeout-ms <ms>|--no-com-timeout] diagnose-com [inputPath]");
            Console.WriteLine("  [--visible] [--keep-open] new-text <outputPath> <text>");
            Console.WriteLine("  [--visible] [--keep-open] copy-save <inputPath> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] export-pdf <inputPath> <outputPdfPath>");
            Console.WriteLine("  [--visible] [--keep-open] doc-info <inputPath>");
            Console.WriteLine("  [--visible] [--keep-open] read-text <inputPath> [outputPath]");
            Console.WriteLine("  [--visible] [--keep-open] read-page <inputPath> <pageNumber> [outputPath]");
            Console.WriteLine("  [--visible] [--keep-open] field-exists <inputPath> <fieldName>");
            Console.WriteLine("  [--visible] [--keep-open] field-get <inputPath> <fieldName>");
            Console.WriteLine("  [--visible] [--keep-open] field-list-raw <inputPath> [option] [optionEx]");
            Console.WriteLine("  [--visible] [--keep-open] list-fields <hwpxFileOrDirectory> [reportMarkdownPath] [--com] [--option value] [--option-ex value]");
            Console.WriteLine("  [--visible] [--keep-open] field-set <inputPath> <fieldName> <text> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] replace-markdown <inputPath> <markdownPath> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] append-markdown-lines <inputPath> <markdownPath> <outputPath> [maxLines]");
            Console.WriteLine("  markdown-submission-text <markdownPath> [outputPath]");
            Console.WriteLine("  markdown-table-list <markdownPath>");
            Console.WriteLine("  table-create-package <inputHwpxPath> <outputHwpxPath> --rows count --cols count [--text row1col1|row1col2;row2col1|row2col2] [--text-file path] [--section sectionName] [--after-anchor text] [--reference-table index] [--border-fill-id id] [--header-border-fill-id id] [--report reportMarkdownPath]");
            Console.WriteLine("  table-row-package <inputHwpxPath> <outputHwpxPath> --table-index index --action add|delete [--row index] [--count count] [--text row1col1|row1col2;row2col1|row2col2] [--text-file path] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    For add, --row inserts after that zero-based row; for delete, --row is the first zero-based row to delete.");
            Console.WriteLine("  table-column-package <inputHwpxPath> <outputHwpxPath> --table-index index --action add|delete [--column index] [--count count] [--text row1col1|row1col2;row2col1|row2col2] [--text-file path] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    For add, --column inserts after that zero-based column; for delete, --column is the first zero-based column to delete.");
            Console.WriteLine("  table-merge-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index --row-span count --col-span count [--text text] [--text-file path] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    --row and --column are the zero-based top-left cell of the rectangular merge region.");
            Console.WriteLine("  table-split-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index [--text row1col1|row1col2;row2col1|row2col2] [--text-file path] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    --row and --column are the zero-based top-left address of the merged cell to split.");
            Console.WriteLine("  table-cell-style-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index --border-fill-id id [--row-span count] [--col-span count] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    Applies an existing header.xml borderFill id to cells intersecting the selected zero-based range.");
            Console.WriteLine("  table-cell-align-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index [--horizontal left|center|right|justify] [--vertical top|center|bottom] [--row-span count] [--col-span count] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    Applies direct cell paragraph horizontal alignment and/or cell subList vertical alignment.");
            Console.WriteLine("  table-cell-background-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index --color #RRGGBB|none [--row-span count] [--col-span count] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    Clones current cell borderFill definitions, changes solid fill color or clears fillBrush, and retargets affected cells.");
            Console.WriteLine("  table-cell-diagonal-package <inputHwpxPath> <outputHwpxPath> --table-index index --row index --column index --direction slash|backslash|both|none [--row-span count] [--col-span count] [--width value] [--color #RRGGBB] [--base-border-fill-id id] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    Clones current cell borderFill definitions, changes diagonal slash/backSlash settings, and retargets affected cells.");
            Console.WriteLine("  table-cell-size-package <inputHwpxPath> <outputHwpxPath> --table-index index [--equalize-widths] [--equalize-heights] [--section sectionName] [--report reportMarkdownPath]");
            Console.WriteLine("    Equalizes column widths and/or row heights in a simple top-level table while preserving total table size.");
            Console.WriteLine("  [--visible] [--keep-open] table-cell-set <inputPath> <outputPath> <tableIndex> <rowMoveCount> <columnMoveCount> <text>");
            Console.WriteLine("  [--visible] [--keep-open] fill-markdown-table <inputPath> <markdownPath> <outputPath> <markdownTableIndex> <hwpTableIndex> [startRow] [startCol] [skipMarkdownRows] [maxRows] [maxCols]");
            Console.WriteLine("  fill-submission-template <templateHwpxPath> <sourceMarkdownPath> <outputHwpxPath> [--profile r-and-d-startup-2026] [--report reportMarkdownPath] [--asset-root directory] [--markdown-table-mode text|render] [--image-mode package|com|none]");
            Console.WriteLine("  extract-form-map <templateHwpxPath> <outputXmlPath>");
            Console.WriteLine("  [--visible] [--keep-open] apply-form-map [--package] <inputHwpxPath> <mapXmlPath> <outputHwpxPath> [maxOperations] [--report reportMarkdownPath]");
            Console.WriteLine("  [--visible] [--keep-open] probe-form-map <inputHwpxPath> <mapXmlPath> <reportMarkdownPath> [maxOperations]");
            Console.WriteLine("  validate-layout <templateHwpxPath> <candidateHwpxPath> [reportMarkdownPath] [--allow-table-row-change indexes] [--allow-table-column-change indexes] [--max-leading-style-drift count]");
            Console.WriteLine("  validate-content <candidateHwpxPath> [reportMarkdownPath] [--require text]...");
            Console.WriteLine("  scan-hwpx-features <hwpxFileOrDirectory> [reportMarkdownPath]");
            Console.WriteLine("  list-pictures <hwpxFileOrDirectory> [reportMarkdownPath]");
            Console.WriteLine("  replace-image-control <inputHwpxPath> <outputHwpxPath> --target control:gso:<index>|picture:<index>|image:<binaryItemIDRef> --image imagePath [--report reportMarkdownPath]");
            Console.WriteLine("  list-header-footer <hwpxFileOrDirectory> [reportMarkdownPath]");
            Console.WriteLine("  set-header-footer-text <inputHwpxPath> <outputHwpxPath> --kind header|footer --anchor text --text replacement [--section sectionName] [--occurrence index] [--report reportMarkdownPath]");
            Console.WriteLine("  [--visible] [--keep-open] page-number-set <inputPath> <outputPath> [--draw-pos value] [--side-char text] [--report reportMarkdownPath]");
            Console.WriteLine("  [--visible] [--keep-open] list-controls <inputPath> [reportMarkdownPath]");
            Console.WriteLine("  [--visible] [--keep-open] probe-copy-from-doc <sourcePath> <targetPath> --source all|paragraph-to-end:<text>|table:<index>|image:<index>|control:<ctrlId>:<index> [--target doc-end|anchor:<text>|cell:<table,rowMove,colMove>|control:<ctrlId>:<index>] [--report reportMarkdownPath]");
            Console.WriteLine("  [--visible] [--keep-open] copy-from-doc <sourcePath> <targetPath> <outputPath> --source all|paragraph-to-end:<text>|table:<index>|image:<index>|control:<ctrlId>:<index> [--target doc-end|anchor:<text>|cell:<table,rowMove,colMove>|control:<ctrlId>:<index>] [--report reportMarkdownPath]");
            Console.WriteLine("  [--visible] [--keep-open] replace-after-marker <inputPath> <markerText> <contentPath> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] replace-text <inputPath> <findText> <replaceText> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] replace-text-batch <inputPath> <outputPath> <findText1> <replaceText1> [<findText2> <replaceText2> ...]");
            Console.WriteLine("  demo-list");
            Console.WriteLine("  [--visible] [--keep-open] demo-feature <featureName> [key=value ...] [--open <path>] [--save <path>]");
            Console.WriteLine("    insertPicture width/height are HWP COM sizing values; values above 1000 are rejected as likely HWPX hp:sz units.");
            Console.WriteLine("  [--visible] [--keep-open] run <command>");
            Console.WriteLine("  [--visible] [--keep-open] action <actionName> [key=value ...] [--open <path>] [--save <path>]");
        }

        private sealed class ComFieldInventoryItem
        {
            public string Name { get; set; }

            public bool Exists { get; set; }

            public string Text { get; set; }

            public string Error { get; set; }
        }
    }
}
