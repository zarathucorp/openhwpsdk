using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenHwp.Automation;

namespace OpenHwp.Automation.Cli
{
    internal static class Program
    {
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
                case "new-text":
                    return NewText(commandArgs, visible, keepOpen);
                case "copy-save":
                    return CopySave(commandArgs, visible, keepOpen);
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

        private static int NewText(string[] args, bool visible, bool keepOpen)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: new-text <outputPath> <text>");
                return 1;
            }

            using (var hwp = CreateSession(visible, keepOpen))
            {
                hwp.ConfigureForAutomation();
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
                hwp.ConfigureForAutomation();
                hwp.Open(args[1]);
                hwp.SaveAs(args[2], string.Empty, string.Empty);
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
                hwp.ConfigureForAutomation();
                hwp.Open(args[1]);
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
                hwp.ConfigureForAutomation();
                hwp.Open(args[1]);

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
                hwp.ConfigureForAutomation();
                hwp.Open(args[1]);

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
                hwp.ConfigureForAutomation();

                if (!string.IsNullOrWhiteSpace(openPath))
                {
                    hwp.Open(openPath);
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
                hwp.ConfigureForAutomation();
                hwp.Open(args[1]);
                hwp.ReplaceDocumentText(plainText);
                hwp.SaveAs(args[3], string.Empty, string.Empty);
            }

            Console.WriteLine(args[3]);
            return 0;
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
                hwp.ConfigureForAutomation();
                hwp.Open(args[1]);

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
            var hwp = HwpSession.Create(visible);
            if (keepOpen)
            {
                hwp.LeaveOpen();
            }

            return hwp;
        }

        private static HwpSession OpenDocument(string inputPath, bool visible, bool keepOpen)
        {
            var hwp = CreateSession(visible, keepOpen);
            try
            {
                hwp.ConfigureForAutomation();
                hwp.Open(inputPath);
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
                hwp.ConfigureForAutomation();
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
                hwp.ConfigureForAutomation();

                if (!string.IsNullOrWhiteSpace(openPath))
                {
                    hwp.Open(openPath);
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

        private static void PrintUsage()
        {
            Console.WriteLine("OpenHwp.Automation.Cli");
            Console.WriteLine("  [--visible] [--keep-open] version");
            Console.WriteLine("  [--visible] [--keep-open] new-text <outputPath> <text>");
            Console.WriteLine("  [--visible] [--keep-open] copy-save <inputPath> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] doc-info <inputPath>");
            Console.WriteLine("  [--visible] [--keep-open] read-text <inputPath> [outputPath]");
            Console.WriteLine("  [--visible] [--keep-open] read-page <inputPath> <pageNumber> [outputPath]");
            Console.WriteLine("  [--visible] [--keep-open] field-exists <inputPath> <fieldName>");
            Console.WriteLine("  [--visible] [--keep-open] field-get <inputPath> <fieldName>");
            Console.WriteLine("  [--visible] [--keep-open] field-list-raw <inputPath> [option] [optionEx]");
            Console.WriteLine("  [--visible] [--keep-open] field-set <inputPath> <fieldName> <text> <outputPath>");
            Console.WriteLine("  [--visible] [--keep-open] replace-markdown <inputPath> <markdownPath> <outputPath>");
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
