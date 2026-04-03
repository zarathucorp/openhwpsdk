using System;
using System.Collections.Generic;
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
            var offset = 0;

            if (args.Length > 0 && string.Equals(args[0], "--visible", StringComparison.OrdinalIgnoreCase))
            {
                visible = true;
                offset = 1;
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
                    return Version(visible);
                case "new-text":
                    return NewText(commandArgs, visible);
                case "copy-save":
                    return CopySave(commandArgs, visible);
                case "field-set":
                    return FieldSet(commandArgs, visible);
                case "run":
                    return RunCommand(commandArgs, visible);
                case "action":
                    return Action(commandArgs, visible);
                default:
                    Console.Error.WriteLine($"Unknown command: {commandArgs[0]}");
                    PrintUsage();
                    return 1;
            }
        }

        private static int Version(bool visible)
        {
            using (var hwp = HwpSession.Create(visible))
            {
                Console.WriteLine(hwp.Version);
                return 0;
            }
        }

        private static int NewText(string[] args, bool visible)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: new-text <outputPath> <text>");
                return 1;
            }

            using (var hwp = HwpSession.Create(visible))
            {
                hwp.ConfigureForAutomation();
                hwp.InsertText(args[2]);
                hwp.SaveAs(args[1], string.Empty, string.Empty);
            }

            Console.WriteLine(args[1]);
            return 0;
        }

        private static int CopySave(string[] args, bool visible)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: copy-save <inputPath> <outputPath>");
                return 1;
            }

            using (var hwp = HwpSession.Create(visible))
            {
                hwp.ConfigureForAutomation();
                hwp.Open(args[1]);
                hwp.SaveAs(args[2], string.Empty, string.Empty);
            }

            Console.WriteLine(args[2]);
            return 0;
        }

        private static int FieldSet(string[] args, bool visible)
        {
            if (args.Length < 5)
            {
                Console.Error.WriteLine("Usage: field-set <inputPath> <fieldName> <text> <outputPath>");
                return 1;
            }

            using (var hwp = HwpSession.Create(visible))
            {
                hwp.ConfigureForAutomation();
                hwp.Open(args[1]);
                hwp.PutFieldText(args[2], args[3]);
                hwp.SaveAs(args[4], string.Empty, string.Empty);
            }

            Console.WriteLine(args[4]);
            return 0;
        }

        private static int RunCommand(string[] args, bool visible)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: run <command>");
                return 1;
            }

            using (var hwp = HwpSession.Create(visible))
            {
                hwp.ConfigureForAutomation();
                hwp.Run(args[1]);
            }

            Console.WriteLine(args[1]);
            return 0;
        }

        private static int Action(string[] args, bool visible)
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

            using (var hwp = HwpSession.Create(visible))
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

        private static void PrintUsage()
        {
            Console.WriteLine("OpenHwp.Automation.Cli");
            Console.WriteLine("  [--visible] version");
            Console.WriteLine("  [--visible] new-text <outputPath> <text>");
            Console.WriteLine("  [--visible] copy-save <inputPath> <outputPath>");
            Console.WriteLine("  [--visible] field-set <inputPath> <fieldName> <text> <outputPath>");
            Console.WriteLine("  [--visible] run <command>");
            Console.WriteLine("  [--visible] action <actionName> [key=value ...] [--open <path>] [--save <path>]");
        }
    }
}
