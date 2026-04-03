using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OpenHwp.Automation
{
    public sealed class HwpSession : IDisposable
    {
        private const string HwpObjectProgId = "HWPFrame.HwpObject";
        private const string FilePathCheckDllModule = "FilePathCheckDLL";
        private static readonly string[] KnownModulePaths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Hnc", "ExCtrl", "Bin", "FilePathCheckerModuleExample.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "HNC", "ExCtrl", "Bin", "FilePathCheckerModuleExample.dll"),
        };

        private object _hwpObject;
        private readonly bool _quitOnDispose;
        private bool _disposed;

        private HwpSession(object hwpObject, bool quitOnDispose)
        {
            _hwpObject = hwpObject ?? throw new ArgumentNullException(nameof(hwpObject));
            _quitOnDispose = quitOnDispose;
        }

        public static HwpSession Create(bool visible = true)
        {
            var type = Type.GetTypeFromProgID(HwpObjectProgId, throwOnError: true);
            if (type == null)
            {
                throw new HwpAutomationException("HWPFrame.HwpObject is not registered on this machine.");
            }

            var session = new HwpSession(Activator.CreateInstance(type), quitOnDispose: true);
            session.Visible = visible;
            return session;
        }

        public static HwpSession AttachToRunningInstance()
        {
            try
            {
                return new HwpSession(Marshal.GetActiveObject(HwpObjectProgId), quitOnDispose: false);
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException("A running HWP automation instance was not found.", ex);
            }
        }

        public static HwpSession ConnectOrCreate(bool visibleWhenCreated = true)
        {
            try
            {
                return new HwpSession(Marshal.GetActiveObject(HwpObjectProgId), quitOnDispose: false);
            }
            catch (COMException)
            {
                return Create(visibleWhenCreated);
            }
        }

        public string Version => Invoke(() => Convert.ToString(((dynamic)_hwpObject).Version), "Read HWP version");

        public string CurrentPath => Invoke(() => Convert.ToString(((dynamic)_hwpObject).Path), "Read current document path");

        public int DocumentCount
        {
            get
            {
                EnsureNotDisposed();
                object documents = null;

                try
                {
                    documents = ((dynamic)_hwpObject).XHwpDocuments;
                    return Convert.ToInt32(((dynamic)documents).Count);
                }
                catch (COMException ex)
                {
                    throw new HwpAutomationException("Failed to read document count.", ex);
                }
                finally
                {
                    ComHelpers.SafeRelease(documents);
                }
            }
        }

        public bool Visible
        {
            get
            {
                object window = null;

                try
                {
                    window = GetActiveWindow();
                    return Convert.ToBoolean(((dynamic)window).Visible);
                }
                catch (COMException ex)
                {
                    throw new HwpAutomationException("Failed to read active window visibility.", ex);
                }
                finally
                {
                    ComHelpers.SafeRelease(window);
                }
            }
            set
            {
                object window = null;

                try
                {
                    window = GetActiveWindow();
                    ((dynamic)window).Visible = value;
                }
                catch (COMException ex)
                {
                    throw new HwpAutomationException("Failed to update active window visibility.", ex);
                }
                finally
                {
                    ComHelpers.SafeRelease(window);
                }
            }
        }

        public int WindowHandle
        {
            get
            {
                object window = null;

                try
                {
                    window = GetActiveWindow();
                    return Convert.ToInt32(((dynamic)window).WindowHandle);
                }
                catch (COMException ex)
                {
                    throw new HwpAutomationException("Failed to read active window handle.", ex);
                }
                finally
                {
                    ComHelpers.SafeRelease(window);
                }
            }
        }

        public HwpRect WindowBounds
        {
            get
            {
                object window = null;

                try
                {
                    window = GetActiveWindow();
                    return new HwpRect(
                        Convert.ToInt32(((dynamic)window).Left),
                        Convert.ToInt32(((dynamic)window).Top),
                        Convert.ToInt32(((dynamic)window).Width),
                        Convert.ToInt32(((dynamic)window).Height));
                }
                catch (COMException ex)
                {
                    throw new HwpAutomationException("Failed to read active window bounds.", ex);
                }
                finally
                {
                    ComHelpers.SafeRelease(window);
                }
            }
        }

        public void SetWindowBounds(HwpRect bounds)
        {
            object window = null;

            try
            {
                window = GetActiveWindow();
                ((dynamic)window).Left = bounds.Left;
                ((dynamic)window).Top = bounds.Top;
                ((dynamic)window).Width = bounds.Width;
                ((dynamic)window).Height = bounds.Height;
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException("Failed to update active window bounds.", ex);
            }
            finally
            {
                ComHelpers.SafeRelease(window);
            }
        }

        public bool TryRegisterFilePathCheckerModule(string modulePath = null)
        {
            var moduleValueName = ResolveFilePathCheckerModuleValueName(modulePath);
            if (string.IsNullOrWhiteSpace(moduleValueName))
            {
                return false;
            }

            try
            {
                return Convert.ToBoolean(((dynamic)_hwpObject).RegisterModule(FilePathCheckDllModule, moduleValueName));
            }
            catch (COMException)
            {
                return false;
            }
        }

        public int GetMessageBoxMode()
        {
            return Invoke(() => Convert.ToInt32(((dynamic)_hwpObject).GetMessageBoxMode()), "Read message box mode");
        }

        public int SetMessageBoxMode(int mode)
        {
            return Invoke(() => Convert.ToInt32(((dynamic)_hwpObject).SetMessageBoxMode(mode)), "Update message box mode");
        }

        public void ConfigureForAutomation(int messageBoxMode = 0x10)
        {
            TryRegisterFilePathCheckerModule();
            SetMessageBoxMode(messageBoxMode);
        }

        public void RegisterModule(string moduleName, string modulePath)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new ArgumentException("Module name is required.", nameof(moduleName));
            }

            if (string.IsNullOrWhiteSpace(modulePath))
            {
                throw new ArgumentException("Module path is required.", nameof(modulePath));
            }

            if (!File.Exists(modulePath))
            {
                throw new FileNotFoundException("The module file was not found.", modulePath);
            }

            try
            {
                var registered = Convert.ToBoolean(((dynamic)_hwpObject).RegisterModule(moduleName, modulePath));
                if (!registered)
                {
                    throw new HwpAutomationException($"RegisterModule returned false for '{moduleName}'.");
                }
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to register module '{moduleName}'.", ex);
            }
        }

        public void Open(string filePath, string format = "", string options = "")
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            try
            {
                var opened = Convert.ToBoolean(((dynamic)_hwpObject).Open(filePath, format ?? string.Empty, options ?? string.Empty));
                if (!opened)
                {
                    throw new HwpAutomationException($"Failed to open '{filePath}'.");
                }
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to open '{filePath}'.", ex);
            }
        }

        public void Save(string options = "")
        {
            try
            {
                var saved = Convert.ToBoolean(((dynamic)_hwpObject).Save(options ?? string.Empty));
                if (!saved)
                {
                    throw new HwpAutomationException("Save returned false.");
                }
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException("Failed to save the current document.", ex);
            }
        }

        public void SaveAs(string filePath, string format = "", string options = "")
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            try
            {
                var saved = Convert.ToBoolean(((dynamic)_hwpObject).SaveAs(filePath, format ?? string.Empty, options ?? string.Empty));
                if (!saved)
                {
                    throw new HwpAutomationException($"SaveAs returned false for '{filePath}'.");
                }
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to save '{filePath}'.", ex);
            }
        }

        public void Run(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("Command name is required.", nameof(command));
            }

            EnsureDocument();

            try
            {
                ((dynamic)_hwpObject).Run(command);
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to run command '{command}'.", ex);
            }
        }

        public bool ExecuteAction(string actionName, Action<HwpParameterSet> configure = null)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                throw new ArgumentException("Action name is required.", nameof(actionName));
            }

            EnsureDocument();
            object actionObject = null;

            try
            {
                actionObject = ((dynamic)_hwpObject).CreateAction(actionName);
                if (actionObject == null)
                {
                    throw new HwpAutomationException($"CreateAction returned null for '{actionName}'.");
                }

                using (var parameterSet = CreateActionParameterSet(actionObject, configure))
                {
                    if (parameterSet == null)
                    {
                        return ToBooleanResult(((dynamic)actionObject).Run(), true);
                    }

                    return Convert.ToBoolean(((dynamic)actionObject).Execute(parameterSet.RawComObject));
                }
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to execute action '{actionName}'.", ex);
            }
            finally
            {
                ComHelpers.SafeRelease(actionObject);
            }
        }

        public void InsertText(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var executed = ExecuteAction("InsertText", set => set.SetItem("Text", text));
            if (!executed)
            {
                throw new HwpAutomationException("InsertText returned false.");
            }
        }

        public string GetFieldText(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Field name is required.", nameof(fieldName));
            }

            EnsureDocument();

            try
            {
                return Convert.ToString(((dynamic)_hwpObject).GetFieldText(fieldName));
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to read field '{fieldName}'.", ex);
            }
        }

        public void PutFieldText(string fieldName, string text)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Field name is required.", nameof(fieldName));
            }

            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            EnsureDocument();

            try
            {
                ((dynamic)_hwpObject).PutFieldText(fieldName, text);
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to write field '{fieldName}'.", ex);
            }
        }

        public string GetPageText(int pageNumber)
        {
            if (pageNumber < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageNumber));
            }

            EnsureDocument();

            try
            {
                return Convert.ToString(((dynamic)_hwpObject).GetPageText(pageNumber, string.Empty));
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to read page text for page {pageNumber}.", ex);
            }
        }

        public void Quit()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                ((dynamic)_hwpObject).Quit();
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException("Failed to quit HWP.", ex);
            }
            finally
            {
                ComHelpers.SafeRelease(_hwpObject);
                _hwpObject = null;
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_quitOnDispose && _hwpObject != null)
            {
                try
                {
                    ((dynamic)_hwpObject).Quit();
                }
                catch
                {
                    // Best effort cleanup only.
                }
            }

            ComHelpers.SafeRelease(_hwpObject);
            _hwpObject = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void EnsureDocument()
        {
            if (DocumentCount > 0)
            {
                return;
            }

            Run("FileNew");
        }

        private object GetActiveWindow()
        {
            EnsureNotDisposed();
            object windows = null;

            try
            {
                windows = ((dynamic)_hwpObject).XHwpWindows;
                return ((dynamic)windows).Active_XHwpWindow;
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException("Failed to resolve the active HWP window.", ex);
            }
            finally
            {
                ComHelpers.SafeRelease(windows);
            }
        }

        private HwpParameterSet CreateActionParameterSet(object actionObject, Action<HwpParameterSet> configure)
        {
            object setObject = null;

            try
            {
                setObject = ((dynamic)actionObject).CreateSet();
                if (setObject == null)
                {
                    if (configure != null)
                    {
                        throw new HwpAutomationException("This action does not expose a parameter set.");
                    }

                    return null;
                }

                ((dynamic)actionObject).GetDefault(setObject);
                var parameterSet = new HwpParameterSet(setObject);
                configure?.Invoke(parameterSet);
                return parameterSet;
            }
            catch
            {
                ComHelpers.SafeRelease(setObject);
                throw;
            }
        }

        private string ResolveFilePathCheckerModuleValueName(string modulePath)
        {
            if (!string.IsNullOrWhiteSpace(modulePath))
            {
                if (File.Exists(modulePath))
                {
                    foreach (var module in EnumerateRegisteredModules())
                    {
                        if (string.Equals(module.Path, modulePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return module.ValueName;
                        }
                    }

                    return null;
                }

                return modulePath;
            }

            foreach (var module in EnumerateRegisteredModules())
            {
                if (File.Exists(module.Path))
                {
                    return module.ValueName;
                }
            }

            foreach (var candidate in KnownModulePaths)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    var valueName = Path.GetFileNameWithoutExtension(candidate);
                    if (!string.IsNullOrWhiteSpace(valueName))
                    {
                        return valueName;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<RegisteredModule> EnumerateRegisteredModules()
        {
            const string modulesKeyPath = @"Software\HNC\HwpAutomation\Modules";

            using (var currentUser = Registry.CurrentUser.OpenSubKey(modulesKeyPath, false))
            {
                if (currentUser != null)
                {
                    foreach (var path in ReadModuleValues(currentUser))
                    {
                        yield return path;
                    }
                }
            }

            using (var localMachine = Registry.LocalMachine.OpenSubKey(modulesKeyPath, false))
            {
                if (localMachine != null)
                {
                    foreach (var path in ReadModuleValues(localMachine))
                    {
                        yield return path;
                    }
                }
            }
        }

        private static IEnumerable<RegisteredModule> ReadModuleValues(RegistryKey key)
        {
            foreach (var valueName in key.GetValueNames())
            {
                var value = key.GetValue(valueName) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return new RegisteredModule(valueName, value);
                }
            }
        }

        private struct RegisteredModule
        {
            public RegisteredModule(string valueName, string path)
            {
                ValueName = valueName;
                Path = path;
            }

            public string ValueName { get; }

            public string Path { get; }
        }

        private static bool ToBooleanResult(object result, bool defaultValue)
        {
            if (result == null)
            {
                return defaultValue;
            }

            if (result is bool)
            {
                return (bool)result;
            }

            try
            {
                return Convert.ToInt32(result) != 0;
            }
            catch
            {
                return defaultValue;
            }
        }

        private T Invoke<T>(Func<T> operation, string operationName)
        {
            EnsureNotDisposed();

            try
            {
                return operation();
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"{operationName} failed.", ex);
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed || _hwpObject == null)
            {
                throw new ObjectDisposedException(nameof(HwpSession));
            }
        }
    }
}
