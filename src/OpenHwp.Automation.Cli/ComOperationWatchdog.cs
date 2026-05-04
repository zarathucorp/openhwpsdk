using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace OpenHwp.Automation.Cli
{
    internal sealed class ComOperationWatchdog : IDisposable
    {
        private readonly object _sync = new object();
        private readonly string _operationName;
        private readonly int _timeoutMs;
        private readonly Timer _timer;
        private string _lastStep;
        private bool _disposed;

        private ComOperationWatchdog(string operationName, int timeoutMs)
        {
            _operationName = string.IsNullOrWhiteSpace(operationName) ? "HWP COM operation" : operationName;
            _timeoutMs = timeoutMs;
            _lastStep = "not started";

            if (timeoutMs > 0)
            {
                _timer = new Timer(OnTimeout, null, timeoutMs, Timeout.Infinite);
            }
        }

        public static ComOperationWatchdog Start(string operationName, int timeoutMs)
        {
            return new ComOperationWatchdog(operationName, timeoutMs);
        }

        public string LastStep
        {
            get
            {
                lock (_sync)
                {
                    return _lastStep;
                }
            }
        }

        public void Step(string step)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _lastStep = string.IsNullOrWhiteSpace(step) ? "unnamed step" : step;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
            }

            if (_timer != null)
            {
                _timer.Dispose();
            }
        }

        internal static string DescribeHwpProcesses()
        {
            try
            {
                var processes = Process.GetProcessesByName("Hwp");
                if (processes.Length == 0)
                {
                    return "none";
                }

                var descriptions = new List<string>();
                foreach (var process in processes)
                {
                    descriptions.Add(DescribeProcess(process));
                    process.Dispose();
                }

                return string.Join("; ", descriptions.ToArray());
            }
            catch (Exception ex)
            {
                return "unavailable: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string DescribeProcess(Process process)
        {
            var description = "pid=" + process.Id;

            try
            {
                if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    description += ", title=" + process.MainWindowTitle;
                }
            }
            catch
            {
                // Process title is best-effort diagnostic data.
            }

            try
            {
                description += ", started=" + process.StartTime.ToString("o");
            }
            catch
            {
                // Process start time may be unavailable for elevated processes.
            }

            return description;
        }

        private void OnTimeout(object state)
        {
            string lastStep;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                lastStep = _lastStep;
                _disposed = true;
            }

            Console.Error.WriteLine("HWP COM operation timed out.");
            Console.Error.WriteLine("operation=" + _operationName);
            Console.Error.WriteLine("timeout_ms=" + _timeoutMs);
            Console.Error.WriteLine("last_step=" + lastStep);
            Console.Error.WriteLine("running_hwp_processes=" + DescribeHwpProcesses());
            Console.Error.Flush();
            Environment.Exit(124);
        }
    }
}
