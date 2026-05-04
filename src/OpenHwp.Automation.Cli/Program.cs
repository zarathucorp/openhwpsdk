using System;
using System.Collections.Generic;
using System.IO;
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
                case "field-set":
                    return FieldSet(commandArgs, visible, keepOpen);
                case "replace-markdown":
                    return ReplaceMarkdown(commandArgs, visible, keepOpen);
                case "append-markdown-lines":
                    return AppendMarkdownLines(commandArgs, visible, keepOpen);
                case "markdown-table-list":
                    return MarkdownTableList(commandArgs);
                case "table-cell-set":
                    return TableCellSet(commandArgs, visible, keepOpen);
                case "fill-markdown-table":
                    return FillMarkdownTable(commandArgs, visible, keepOpen);
                case "extract-form-map":
                    return ExtractFormMap(commandArgs);
                case "apply-form-map":
                    return ApplyFormMap(commandArgs, visible, keepOpen);
                case "probe-form-map":
                    return ProbeFormMap(commandArgs, visible, keepOpen);
                case "validate-layout":
                    return ValidateLayout(commandArgs);
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

        private static int ApplyFormMap(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: apply-form-map <inputHwpxPath> <mapXmlPath> <outputHwpxPath> [maxOperations]");
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

            HwpxFormMap.ApplyResult result;
            using (var hwp = CreateSession(visible, keepOpen))
            {
                ConfigureSessionForAutomation(hwp);
                OpenSessionDocument(hwp, args[1], string.Empty, "forceopen:true");
                result = HwpxFormMap.Apply(hwp, args[2], maxOperations);
                hwp.SaveAs(args[3], ResolveSaveFormat(args[3]), string.Empty);
            }

            Console.WriteLine(args[3]);
            Console.WriteLine("attempted=" + result.Attempted);
            Console.WriteLine("applied=" + result.Applied);
            Console.WriteLine("failed=" + result.Failed);
            Console.WriteLine("skipped_unsafe=" + result.SkippedUnsafe);
            return result.Failed == 0 ? 0 : 2;
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
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: validate-layout <templateHwpxPath> <candidateHwpxPath> [reportMarkdownPath]");
                return 1;
            }

            var passed = HwpxLayoutValidator.Validate(args[1], args[2], args.Length >= 4 ? args[3] : null);
            return passed ? 0 : 2;
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

        private static string BoolText(bool value)
        {
            return value.ToString().ToLowerInvariant();
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
            Console.WriteLine("  [--visible] [--keep-open] field-set <inputPath> <fieldName> <text> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] replace-markdown <inputPath> <markdownPath> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] append-markdown-lines <inputPath> <markdownPath> <outputPath> [maxLines]");
            Console.WriteLine("  markdown-table-list <markdownPath>");
            Console.WriteLine("  [--visible] [--keep-open] table-cell-set <inputPath> <outputPath> <tableIndex> <rowMoveCount> <columnMoveCount> <text>");
            Console.WriteLine("  [--visible] [--keep-open] fill-markdown-table <inputPath> <markdownPath> <outputPath> <markdownTableIndex> <hwpTableIndex> [startRow] [startCol] [skipMarkdownRows] [maxRows] [maxCols]");
            Console.WriteLine("  extract-form-map <templateHwpxPath> <outputXmlPath>");
            Console.WriteLine("  [--visible] [--keep-open] apply-form-map <inputHwpxPath> <mapXmlPath> <outputHwpxPath> [maxOperations]");
            Console.WriteLine("  [--visible] [--keep-open] probe-form-map <inputHwpxPath> <mapXmlPath> <reportMarkdownPath> [maxOperations]");
            Console.WriteLine("  validate-layout <templateHwpxPath> <candidateHwpxPath> [reportMarkdownPath]");
            Console.WriteLine("  [--visible] [--keep-open] replace-after-marker <inputPath> <markerText> <contentPath> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] replace-text <inputPath> <findText> <replaceText> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] replace-text-batch <inputPath> <outputPath> <findText1> <replaceText1> [<findText2> <replaceText2> ...]");
            Console.WriteLine("  demo-list");
            Console.WriteLine("  [--visible] [--keep-open] demo-feature <featureName> [key=value ...] [--open <path>] [--save <path>]");
            Console.WriteLine("  [--visible] [--keep-open] run <command>");
            Console.WriteLine("  [--visible] [--keep-open] action <actionName> [key=value ...] [--open <path>] [--save <path>]");
        }
    }
}
