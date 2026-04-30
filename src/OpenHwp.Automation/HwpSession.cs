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
        private bool _quitOnDispose;
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

        public void LeaveOpen()
        {
            EnsureNotDisposed();
            _quitOnDispose = false;
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

        public int PageCount => Invoke(() => Convert.ToInt32(((dynamic)_hwpObject).PageCount), "Read page count");

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

        public int EditMode
        {
            get { return Invoke(() => Convert.ToInt32(((dynamic)_hwpObject).EditMode), "Read edit mode"); }
            set { Invoke(() => { ((dynamic)_hwpObject).EditMode = value; return true; }, "Update edit mode"); }
        }

        public int SelectionMode => Invoke(() => Convert.ToInt32(((dynamic)_hwpObject).SelectionMode), "Read selection mode");

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

            var resolvedPath = Path.GetFullPath(filePath);

            try
            {
                var opened = Convert.ToBoolean(((dynamic)_hwpObject).Open(resolvedPath, format ?? string.Empty, options ?? string.Empty));
                if (!opened)
                {
                    throw new HwpAutomationException($"Failed to open '{resolvedPath}'.");
                }
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to open '{resolvedPath}'.", ex);
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

            var resolvedPath = Path.GetFullPath(filePath);

            try
            {
                var saved = Convert.ToBoolean(((dynamic)_hwpObject).SaveAs(resolvedPath, format ?? string.Empty, options ?? string.Empty));
                if (!saved)
                {
                    throw new HwpAutomationException($"SaveAs returned false for '{resolvedPath}'.");
                }
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to save '{resolvedPath}'.", ex);
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
            return ExecuteAction(actionName, null, configure);
        }

        public bool ExecuteAction(string actionName, string setId, Action<HwpParameterSet> configure)
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

                using (var parameterSet = CreateActionParameterSet(actionObject, setId, configure))
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

        public void InsertPicture(
            string path,
            bool embedded = true,
            int sizeOption = 0,
            bool reverse = false,
            bool watermark = false,
            int effect = 0,
            int width = 200,
            int height = 200)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Picture path is required.", nameof(path));
            }

            EnsureDocument();

            try
            {
                ((dynamic)_hwpObject).InsertPicture(
                    Path.GetFullPath(path),
                    embedded,
                    sizeOption,
                    reverse,
                    watermark,
                    effect,
                    width,
                    height);
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException("Failed to insert picture.", ex);
            }
        }

        public bool InsertTable(int rows = 4, int cols = 4, bool treatAsChar = true)
        {
            if (rows <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            if (cols <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cols));
            }

            return ExecuteAction(
                "TableCreate",
                set =>
                {
                    set.SetItem("Rows", rows);
                    set.SetItem("Cols", cols);
                    set.SetItem("TreatAsChar", treatAsChar ? 1 : 0);
                });
        }

        public bool AddTableRow(int side = 3, int count = 1, int tableIndex = -1)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (tableIndex >= 0 && !SelectTableCell(0, 0, tableIndex))
                    {
                        return false;
                    }

                    return ExecuteAction(
                        "TableInsertRowColumn",
                        "TableInsertLine",
                        set =>
                        {
                            set.SetItem("Side", side);
                            set.SetItem("Count", count);
                        });
                });
        }

        public bool AddTableColumn(int side = 1, int count = 1, int tableIndex = -1)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (tableIndex >= 0 && !SelectTableCell(0, 0, tableIndex))
                    {
                        return false;
                    }

                    return ExecuteAction(
                        "TableInsertRowColumn",
                        "TableInsertLine",
                        set =>
                        {
                            set.SetItem("Side", side);
                            set.SetItem("Count", count);
                        });
                });
        }

        public bool DeleteTableRow(int tableIndex = -1)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (tableIndex >= 0 && !SelectTableCell(0, 0, tableIndex))
                    {
                        return false;
                    }

                    return TryRunCommand("TableDeleteRow");
                });
        }

        public bool DeleteTableColumn(int tableIndex = -1)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (tableIndex >= 0 && !SelectTableCell(0, 0, tableIndex))
                    {
                        return false;
                    }

                    return TryRunCommand("TableDeleteColumn");
                });
        }

        public bool SelectTableCell(int rowMoveCount = 0, int columnMoveCount = 0, int tableIndex = 0)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (!SelectTable(tableIndex))
                    {
                        return false;
                    }

                    if (!TryRunCommand("ShapeObjTableSelCell"))
                    {
                        return false;
                    }

                    for (var i = 0; i < rowMoveCount; i++)
                    {
                        if (!TryRunCommand("TableLowerCell"))
                        {
                            return false;
                        }
                    }

                    for (var i = 0; i < columnMoveCount; i++)
                    {
                        if (!TryRunCommand("TableRightCell"))
                        {
                            return false;
                        }
                    }

                    return true;
                });
        }

        public bool SelectAllTableCells(int tableIndex = -1)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (tableIndex >= 0 && !SelectTableCell(0, 0, tableIndex))
                    {
                        return false;
                    }

                    return TryRunCommand("TableCellBlock") &&
                           TryRunCommand("TableCellBlockExtend") &&
                           TryRunCommand("TableCellBlockExtend");
                });
        }

        public bool SetCellBackgroundColor(byte red, byte green, byte blue, int tableIndex = -1)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (tableIndex >= 0 && !SelectTableCell(0, 0, tableIndex))
                    {
                        return false;
                    }

                    TryRunCommand("TableCellBlock");

                    return ExecuteAction(
                        "CellBorderFill",
                        set =>
                        {
                            using (var fillAttr = GetOrCreateChildSet(set, "FillAttr", "DrawFillAttr"))
                            {
                                var type = fillAttr.ItemExists("Type") ? ToInt(fillAttr.GetItem("Type"), 0) : 0;
                                fillAttr.SetItem("Type", type | 1);
                                fillAttr.SetItem("WinBrushHatchColor", ToColorRef(0, 0, 0));
                                fillAttr.SetItem("WinBrushFaceColor", ToColorRef(red, green, blue));
                                fillAttr.SetItem("WinBrushFaceStyle", -1);
                            }
                        });
                });
        }

        public bool SetCellBorder(int borderWidth = 12, int borderType = 11, int tableIndex = -1)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (tableIndex >= 0 && !SelectAllTableCells(tableIndex))
                    {
                        return false;
                    }

                    return ExecuteAction(
                        "CellBorder",
                        "CellBorderFill",
                        set =>
                        {
                            set.SetItem("BorderWidthBottom", borderWidth);
                            set.SetItem("BorderTypeBottom", borderType);
                            set.SetItem("BorderWidthTop", borderWidth);
                            set.SetItem("BorderTypeTop", borderType);
                            set.SetItem("BorderWidthRight", borderWidth);
                            set.SetItem("BorderTypeRight", borderType);
                            set.SetItem("BorderWidthLeft", borderWidth);
                            set.SetItem("BorderTypeLeft", borderType);
                        });
                });
        }

        public bool ResizeTableCellWidth(string mode, int tableIndex = -1)
        {
            if (tableIndex >= 0 && !SelectAllTableCells(tableIndex))
            {
                return false;
            }

            return RunResizeCommand(mode, "TableResizeCellRight", "TableResizeCellLeft", "TableDistributeCellWidth");
        }

        public bool ResizeTableCellHeight(string mode, int tableIndex = -1)
        {
            if (tableIndex >= 0 && !SelectAllTableCells(tableIndex))
            {
                return false;
            }

            return RunResizeCommand(mode, "TableResizeCellDown", "TableResizeCellUp", "TableDistributeCellHeight");
        }

        public bool MergeTableCells(int tableIndex = -1)
        {
            if (tableIndex >= 0 && !SelectAllTableCells(tableIndex))
            {
                return false;
            }

            return ExecuteAction("TableMergeCell");
        }

        public bool SplitTableCell(int rows = 3, int cols = 3, int tableIndex = -1)
        {
            if (tableIndex >= 0 && !SelectTableCell(0, 0, tableIndex))
            {
                return false;
            }

            return ExecuteAction(
                "TableSplitCell",
                set =>
                {
                    set.SetItem("Rows", rows);
                    set.SetItem("Cols", cols);
                });
        }

        public bool DeleteCellText(int tableIndex = -1)
        {
            return WithEditMode(
                1,
                () =>
                {
                    if (tableIndex >= 0 && !SelectTableCell(0, 0, tableIndex))
                    {
                        return false;
                    }

                    TryRunCommand("TableCellBlock");
                    return ExecuteAction("TableDeleteCell");
                });
        }

        public bool SetTableCellText(int tableIndex, int rowMoveCount, int columnMoveCount, string text)
        {
            if (rowMoveCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rowMoveCount));
            }

            if (columnMoveCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columnMoveCount));
            }

            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return WithEditMode(
                1,
                () =>
                {
                    if (!SelectTableCell(rowMoveCount, columnMoveCount, tableIndex))
                    {
                        return false;
                    }

                    TryRunCommand("TableCellBlock");
                    ExecuteAction("TableDeleteCell");
                    InsertText(text);
                    return true;
                });
        }

        public bool InsertPageBreak()
        {
            return TryRunCommand("BreakPage");
        }

        public bool AddPageNumbering(int drawPos = 5, string sideChar = "-")
        {
            return ExecuteAction(
                "PageNumPos",
                set =>
                {
                    set.SetItem("DrawPos", drawPos);
                    set.SetItem("SideChar", sideChar ?? string.Empty);
                });
        }

        public bool SetCharShape(int fontSizePoints = 20, bool bold = true)
        {
            return SetCharShapeHeight(fontSizePoints * 100, bold);
        }

        public bool SetCharShapeHeight(int height, bool bold = true, int? textColor = null)
        {
            return ExecuteAction(
                "CharShape",
                set =>
                {
                    set.SetItem("Height", height);
                    set.SetItem("Bold", bold ? 1 : 0);
                    if (textColor.HasValue)
                    {
                        set.SetItem("TextColor", textColor.Value);
                    }
                });
        }

        public bool CopyShape(int type = 0)
        {
            return ExecuteAction(
                "ShapeCopyPaste",
                set => set.SetItem("Type", type));
        }

        public bool PasteCopiedShape()
        {
            if (SelectionMode <= 0)
            {
                return false;
            }

            return TryRunCommand("ShapeCopyPaste");
        }

        public void ReplaceDocumentText(string text, int chunkSize = 4096)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            EnsureDocument();
            TryRunCommand("MoveDocBegin");
            TryRunCommand("SelectAll");

            if (!TryRunCommand("Delete"))
            {
                TryRunCommand("DeleteBack");
            }

            InsertTextChunks(text, chunkSize);
        }

        public bool ReplaceFromMarkerToDocumentEnd(string markerText, string replacementText, int chunkSize = 4096)
        {
            if (string.IsNullOrWhiteSpace(markerText))
            {
                throw new ArgumentException("Marker text is required.", nameof(markerText));
            }

            if (replacementText == null)
            {
                throw new ArgumentNullException(nameof(replacementText));
            }

            EnsureDocument();
            TryRunCommand("MoveDocBegin");

            if (!TryFind(markerText))
            {
                return false;
            }

            TryRunCommand("MoveParaBegin");
            TryRunCommand("Select");
            TryRunCommand("MoveSelDocEnd");

            if (!TryRunCommand("Delete"))
            {
                TryRunCommand("DeleteBack");
            }

            InsertTextChunks(replacementText, chunkSize);
            return true;
        }

        public bool ReplaceAllText(string findText, string replaceText, bool wholeWordOnly = false)
        {
            if (string.IsNullOrWhiteSpace(findText))
            {
                throw new ArgumentException("Find text is required.", nameof(findText));
            }

            if (replaceText == null)
            {
                throw new ArgumentNullException(nameof(replaceText));
            }

            EnsureDocument();
            object actionObject = null;

            try
            {
                actionObject = ((dynamic)_hwpObject).CreateAction("AllReplace");
                if (actionObject == null)
                {
                    throw new HwpAutomationException("CreateAction returned null for 'AllReplace'.");
                }

                using (var parameterSet = CreateActionParameterSet(actionObject, set =>
                       {
                           set.SetItem("FindString", findText);
                           set.SetItem("ReplaceString", replaceText);
                           set.SetItem("IgnoreMessage", 1);
                           set.SetItem("Direction", 2);
                           set.SetItem("MatchCase", 0);
                           set.SetItem("AllWordForms", 0);
                           set.SetItem("SeveralWords", 0);
                           set.SetItem("UseWildCards", 0);
                           set.SetItem("WholeWordOnly", wholeWordOnly ? 1 : 0);
                           set.SetItem("AutoSpell", 1);
                           set.SetItem("IgnoreFindString", 0);
                           set.SetItem("IgnoreReplaceString", 0);
                           set.SetItem("ReplaceMode", 1);
                           set.SetItem("HanjaFromHangul", 0);
                           set.SetItem("FindJaso", 0);
                           set.SetItem("FindRegExp", 0);
                           set.SetItem("FindStyle", string.Empty);
                           set.SetItem("ReplaceStyle", string.Empty);
                           set.SetItem("FindType", 1);
                       }))
                {
                    return Convert.ToBoolean(((dynamic)actionObject).Execute(parameterSet.RawComObject));
                }
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException("Failed to replace text in document.", ex);
            }
            finally
            {
                ComHelpers.SafeRelease(actionObject);
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

        public bool FieldExists(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Field name is required.", nameof(fieldName));
            }

            EnsureDocument();

            try
            {
                return Convert.ToBoolean(((dynamic)_hwpObject).FieldExist(fieldName));
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to check field '{fieldName}'.", ex);
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

        public string GetDocumentText(string format = "TEXT", string options = "")
        {
            EnsureDocument();
            var resolvedFormat = format ?? "TEXT";
            var resolvedOptions = options ?? string.Empty;

            if (string.IsNullOrWhiteSpace(resolvedOptions) && string.Equals(resolvedFormat, "TEXT", StringComparison.OrdinalIgnoreCase))
            {
                resolvedOptions = "utf8";
            }

            try
            {
                return Convert.ToString(((dynamic)_hwpObject).GetTextFile(resolvedFormat, resolvedOptions));
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException($"Failed to read document text using format '{resolvedFormat}'.", ex);
            }
        }

        public string GetFieldListRaw(int option = 0, int optionEx = 0)
        {
            EnsureDocument();

            try
            {
                return Convert.ToString(((dynamic)_hwpObject).GetFieldList(option, optionEx));
            }
            catch (COMException ex)
            {
                throw new HwpAutomationException("Failed to read field list.", ex);
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
            return CreateActionParameterSet(actionObject, null, configure);
        }

        private HwpParameterSet CreateActionParameterSet(object actionObject, string setId, Action<HwpParameterSet> configure)
        {
            object setObject = null;

            try
            {
                setObject = string.IsNullOrWhiteSpace(setId)
                    ? ((dynamic)actionObject).CreateSet()
                    : ((dynamic)_hwpObject).CreateSet(setId);
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

        private bool TryRunCommand(string command)
        {
            try
            {
                ((dynamic)_hwpObject).Run(command);
                return true;
            }
            catch (COMException)
            {
                return false;
            }
        }

        private bool RunResizeCommand(string mode, string increaseCommand, string decreaseCommand, string distributeCommand)
        {
            var normalizedMode = (mode ?? "increase").Trim().ToLowerInvariant();
            var command = increaseCommand;

            if (normalizedMode == "decrease")
            {
                command = decreaseCommand;
            }
            else if (normalizedMode == "distribute" || normalizedMode == "equal")
            {
                command = distributeCommand;
            }

            return TryRunCommand(command);
        }

        private bool SelectTable(int tableIndex)
        {
            object ctrl = null;
            var currentIndex = 0;

            try
            {
                ctrl = ((dynamic)_hwpObject).HeadCtrl;
                while (ctrl != null)
                {
                    object next = null;
                    object anchor = null;

                    try
                    {
                        var ctrlId = Convert.ToString(((dynamic)ctrl).CtrlID);
                        next = ((dynamic)ctrl).Next;

                        if (string.Equals(ctrlId, "tbl", StringComparison.OrdinalIgnoreCase))
                        {
                            if (currentIndex == tableIndex)
                            {
                                anchor = ((dynamic)ctrl).GetAnchorPos(0);
                                if (anchor != null && !Convert.ToBoolean(((dynamic)_hwpObject).SetPosBySet(anchor)))
                                {
                                    return false;
                                }

                                return TryRunCommand("SelectCtrlReverse");
                            }

                            currentIndex++;
                        }
                    }
                    finally
                    {
                        ComHelpers.SafeRelease(anchor);
                        ComHelpers.SafeRelease(ctrl);
                    }

                    ctrl = next;
                }

                return false;
            }
            catch (COMException)
            {
                return false;
            }
            finally
            {
                ComHelpers.SafeRelease(ctrl);
            }
        }

        private static int ToColorRef(byte red, byte green, byte blue)
        {
            return red | (green << 8) | (blue << 16);
        }

        private static int ToInt(object value, int defaultValue)
        {
            if (value == null)
            {
                return defaultValue;
            }

            if (value is UIntPtr)
            {
                try
                {
                    return unchecked((int)((UIntPtr)value).ToUInt64());
                }
                catch
                {
                    return defaultValue;
                }
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                int parsedValue;
                return int.TryParse(Convert.ToString(value), out parsedValue) ? parsedValue : defaultValue;
            }
        }

        private static HwpParameterSet GetOrCreateChildSet(HwpParameterSet set, string name, string setId)
        {
            if (set.ItemExists(name))
            {
                return new HwpParameterSet(set.GetItem(name));
            }

            return set.CreateChildSet(name, setId);
        }

        private T WithEditMode<T>(int editMode, Func<T> action)
        {
            var originalEditMode = EditMode;

            try
            {
                EditMode = editMode;
                return action();
            }
            finally
            {
                try
                {
                    EditMode = originalEditMode;
                }
                catch
                {
                    // Best effort restoration only.
                }
            }
        }

        private void InsertTextChunks(string text, int chunkSize)
        {
            for (var startIndex = 0; startIndex < text.Length; startIndex += chunkSize)
            {
                var length = Math.Min(chunkSize, text.Length - startIndex);
                InsertText(text.Substring(startIndex, length));
            }
        }

        private bool TryFind(string findText)
        {
            object actionObject = null;

            try
            {
                actionObject = ((dynamic)_hwpObject).CreateAction("RepeatFind");
                if (actionObject == null)
                {
                    return false;
                }

                using (var parameterSet = CreateActionParameterSet(actionObject, set =>
                       {
                           set.SetItem("FindString", findText);
                           set.SetItem("ReplaceString", string.Empty);
                           set.SetItem("IgnoreReplaceString", 0);
                           set.SetItem("IgnoreFindString", 0);
                           set.SetItem("Direction", 1);
                           set.SetItem("WholeWordOnly", 0);
                           set.SetItem("UseWildCards", 0);
                           set.SetItem("SeveralWords", 0);
                           set.SetItem("AllWordForms", 0);
                           set.SetItem("MatchCase", 0);
                           set.SetItem("ReplaceMode", 0);
                           set.SetItem("ReplaceStyle", string.Empty);
                           set.SetItem("FindStyle", string.Empty);
                           set.SetItem("FindRegExp", 0);
                           set.SetItem("FindJaso", 0);
                           set.SetItem("HanjaFromHangul", 0);
                           set.SetItem("IgnoreMessage", 1);
                           set.SetItem("FindType", 1);
                       }))
                {
                    return Convert.ToBoolean(((dynamic)actionObject).Execute(parameterSet.RawComObject));
                }
            }
            catch (COMException)
            {
                return false;
            }
            finally
            {
                ComHelpers.SafeRelease(actionObject);
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
