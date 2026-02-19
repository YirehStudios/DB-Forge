/*
    DBF Forge - Main Controller
    Copyright (C) 2026 YirehStudios
    
    Update: Forensic Log System + Dynamic Theme Engine
    Description: 
    Central controller that orchestrates the UI, dependency injection for file reading, 
    and the data export pipeline (The "Hopper").
*/

using Godot;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FastDBF = SocialExplorer.IO.FastDBF; 
using DbfDataReader;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

// Importing core components refactored in previous steps
using DbfForge.Core.Readers;
using DbfForge.UI.Components;

namespace DbfForge.UI
{
    /// <summary>
    /// Main class managing the Menu interface and business logic 
    /// for converting files (Excel, CSV, ODS) to DBF format.
    /// </summary>
    public partial class MainMenu : Control
    {
        // --- UI REFERENCES ---
        [Export] public PackedScene SheetCardPrefab;
        [Export] public VBoxContainer SheetListVBox; // Renamed from SheetListContainer
        [Export] public Button ProcessFilesBtn; // Renamed from BtnExport
        [Export] public PanelContainer LoadingToast; // Renamed from LoadingScreen
        [Export] public Label ProcessingLabel; // Renamed from LblStatus
        [Export] public FileDialog OpenFileDialog; // Renamed from SystemFileDialog
        [Export] public CheckButton MergeSwitch; // Renamed from CheckMerge
        [Export] public Button ClearQueueBtn; // Renamed from BtnClearList
        [Export] public Label FileQueueLabel; // Renamed from LblCounter
        
        // Empty State & Navigation
        [Export] public PanelContainer DropZonePanel; // Renamed from EmptyStatePanel
        [Export] public Button BrowseFilesBtn; // Renamed from BtnCenterSearch
        
        // Footer & Output Configuration
        [Export] public Label PathLabel; // Renamed from LblOutputPath
        [Export] public Button ChangePathBtn; // Renamed from BtnChangeOutput
        [Export] public OptionButton MasterFormatDropdown; // Renamed from OptMasterFormat
        [Export] public LineEdit MergeFilenameInput; // Renamed from EditNombreFusion
        [Export] public ProgressBar GlobalProgressBar; 
        
        // Sidebar Navigation
        [Export] public Button OpenOutputBtn; // Renamed from BtnSidebarOpenFolder
        [Export] public Button ClearAllBtn; // Renamed from BtnSidebarClear
        [Export] public Button ViewLogsBtn; // Renamed from BtnSidebarLogs
        [Export] public Button SettingsBtn; // Renamed from BtnSidebarSettings
        [Export] public Button HelpBtn; // New button for help documentation
        [Export] public Button AboutBtn; // Renamed from BtnSidebarAbout
        
        // Modals & Inspector
        [Export] public MarginContainer SettingsView; // Renamed from ModalSettings
        [Export] public CheckButton DarkModeSwitch; // Renamed from CheckDarkMode
        [Export] public VBoxContainer LogsView; // Renamed from ModalLogs
        [Export] public RichTextLabel LogRichText; // Renamed from TextLogs
        [Export] public MarginContainer AboutView; // Renamed from ModalAbout
        [Export] public Control InspectorOverlay;
        [Export] public PanelContainer InspectorPanel;
        [Export] public Button CloseInspectorBtn;

        // --- INTERNAL VARIABLES ---
        private bool _isViewingIndividualLogs = false;
        private AnimationPlayer _animPlayer;
        private PanelContainer _inspectorSlide; // Keeping for compatibility if logic needs it, though exported InspectorPanel handles the panel itself

        private string _globalOutputPath = "";
        private bool _isSelectingOutputFolder = false;
        private readonly List<string> _systemLogs = new List<string>();

        // Allowed extensions for file filter
        private readonly HashSet<string> _validExtensions = new() { ".xlsx", ".xls", ".csv", ".txt", ".ods", ".dbf"};

        #region Data Transfer Objects (DTOs)
        
        /// <summary>
        /// Represents a spreadsheet or flat file loaded in memory before processing.
        /// </summary>
        public class SheetData {
            public string SourceFileName;
            public string SourceFullPath;
            public string SheetName;
            public List<object[]> RawRows = new List<object[]>();
            public List<object[]> DebugSample = new List<object[]>(); 
            public List<DetectedColumn> Structure = new List<DetectedColumn>();
        }

        /// <summary>
        /// Defines inferred column properties (type, length, decimals).
        /// </summary>
        public class DetectedColumn {
            public string OriginalName;
            public int SuggestedType; 
            public int Length;
            public int Decimals;
            public bool IsGhostColumn;
        }

        /// <summary>
        /// Object containing all necessary information to perform the final export.
        /// </summary>
        public class ForgeTicket {
            public SheetCard UICard; 
            public string BasePath;
            public string FinalPathUsed;
            public List<DetectedColumn> ColumnDefinitions;
            public List<object[]> DataReadyForDBF;
            public int OutputFormat = 0; 
        }
        
        #endregion

        // --- CONFIGURATION & PERSISTENCE ---
        private readonly string _configPath = "user://config.json";
        
        public class UserConfig {
            public string LastOutputPath { get; set; } = "";
            public bool IsDarkMode { get; set; } = true;
        }
        
        private UserConfig _currentConfig = new UserConfig();
        private StreamWriter _masterLogFile;
        private readonly object _logLock = new object();

        // ==========================================================================================
        // INITIALIZATION AND LIFECYCLE
        // ==========================================================================================

        /// <summary>
        /// Initializes the environment, loads user preferences, and establishes UI event connections.
        /// </summary>
        
        public override void _Ready()
        {
            // Configure encoding for legacy support (Windows-1252)
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            
            // Subscribe to system events
            GetWindow().FilesDropped += OnFilesDropped;
            
            // Initialize node references
            _animPlayer = GetNodeOrNull<AnimationPlayer>("AnimPlayer");
            
            // --- PHYSICAL LOGGING SYSTEM INITIALIZATION (MASTER LOG) ---
            try 
            {
                string logDir = ProjectSettings.GlobalizePath("user://logs");
                
                // Verify directory integrity
                if (!System.IO.Directory.Exists(logDir)) 
                    System.IO.Directory.CreateDirectory(logDir);

                // Naming based on timestamp for session uniqueness
                string fileName = $"Forge_Session_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                string fullPath = Path.Combine(logDir, fileName);
                
                // FileShare.Read allows external reading while the app writes to it
                // Explicit System.IO usage to avoid conflict with Godot.FileAccess
                var fs = new FileStream(fullPath, FileMode.Append, System.IO.FileAccess.Write, FileShare.Read);
                
                // AutoFlush = true guarantees immediate persistence in case of a crash
                _masterLogFile = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };

                // Session Forensic Header
                lock (_logLock)
                {
                    _masterLogFile.WriteLine("===============================================================================");
                    _masterLogFile.WriteLine($" SYSTEM BOOT | {DateTime.Now:O}");
                    _masterLogFile.WriteLine($" ENVIRONMENT | OS: {OS.GetName()} | Locale: {System.Globalization.CultureInfo.CurrentCulture.Name}");
                    _masterLogFile.WriteLine("===============================================================================");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"CRITICAL FAILURE INITIALIZING LOGGER: {ex.Message}");
            }

            // Load persistent configuration
            LoadPreferences();
            LogSys("System initialized. Configuration loaded.");

            // --- UI CONFIGURATION AND EVENTS ---

            // 1. File Dialog Configuration
            if (OpenFileDialog != null) {
                OpenFileDialog.FileSelected += (p) => { LogUser($"Selection: {Path.GetFileName(p)}"); OnFileSelected(p); };
                OpenFileDialog.FilesSelected += (ps) => { LogUser($"Multiple selection: {ps.Length}"); OnFilesSelected(ps); };
                OpenFileDialog.DirSelected += OnFolderSelected;
            }

            // 2. Primary Action Button Connections
            ConnectButton(BrowseFilesBtn, "Browse Files", OpenFileBrowser);
            ConnectButton(ProcessFilesBtn, "Export", OnExportButtonPressed);
            
            // 3. Output Path Management
            if (ChangePathBtn != null) {
                ChangePathBtn.Pressed += AbrirSeleccionCarpeta;
            }

            // 4. Master Format Configuration
            if (MasterFormatDropdown != null) {
                MasterFormatDropdown.Clear();
                MasterFormatDropdown.AddItem("[ Individual ]", 99); 
                MasterFormatDropdown.AddItem("Format DBF (.dbf)", 0);     
                MasterFormatDropdown.AddItem("Format Excel (.xlsx)", 1);   
                MasterFormatDropdown.AddItem("Format CSV (.csv)", 2);     
                
                MasterFormatDropdown.Selected = 0; 
                MasterFormatDropdown.ItemSelected += OnMasterFormatChanged;
            }

            // 5. Sidebar Navigation Configuration
            ConnectButton(OpenOutputBtn, "Open Folder", OpenSmartFolder);
            ConnectButton(ClearAllBtn, "Clear All", ClearAll);
            ConnectButton(ClearQueueBtn, "Clear List", ClearAll);
            ConnectButton(HelpBtn, "Open Documentation", OpenLocalDocumentation);

            // 6. Panel Management (Inspector and Sidebar)
            if(ViewLogsBtn != null) ViewLogsBtn.Pressed += () => {
                _isViewingIndividualLogs = false; 
                if(LogRichText != null) LogRichText.Text = string.Join("\n", _systemLogs);
                ToggleInspector(LogsView);
            };
            
            ConnectButton(SettingsBtn, "Settings", () => ToggleInspector(SettingsView));
            ConnectButton(AboutBtn, "About", () => ToggleInspector(AboutView));
            ConnectButton(CloseInspectorBtn, "Close Inspector", CerrarInspector);

            if (InspectorOverlay != null) {
                InspectorOverlay.MouseFilter = MouseFilterEnum.Stop; 
                InspectorOverlay.GuiInput += (ev) => {
                    if (ev is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left) 
                        CerrarInspector();
                };
            }

            // 7. Log Interactivity Configuration
            if (LogRichText != null) {
                LogRichText.SelectionEnabled = true;
                LogRichText.ContextMenuEnabled = true;
            }

            // State Toggles Configuration
            if (MergeSwitch != null) MergeSwitch.Toggled += (v) => { OnMergeToggled(v); LogUser($"Merge: {v}"); };
            
            if (DarkModeSwitch != null) {
                DarkModeSwitch.ButtonPressed = _currentConfig.IsDarkMode; 
                DarkModeSwitch.Toggled += (v) => { OnThemeToggled(v); LogUser($"Dark Mode: {v}"); };
                ApplyGlobalTheme(_currentConfig.IsDarkMode);
            }

            // Initial visual state
            _globalOutputPath = _currentConfig.LastOutputPath;
            UpdatePathLabel();
            ToggleLoading(false);
            UpdateCounter();
        }

        private void OnMasterFormatChanged(long index)
        {
            if (MasterFormatDropdown == null) return;

            // Retrieve real ID assigned (0=DBF, 1=Excel, 2=CSV, 99=Individual)
            int selectedId = MasterFormatDropdown.GetItemId((int)index);

            // If ID is 99 (Individual), do not force changes on cards
            if (selectedId == 99) return;

            LogUser($"Applying global format to all sheets: {MasterFormatDropdown.GetItemText((int)index)}");
            
            // Propagate format to all active cards
            foreach(Node n in SheetListVBox.GetChildren()) {
                if (n is SheetCard card && card.FormatDropdown != null) {
                    // Temporarily disable listener to avoid event loop
                    card.OnManualFormatChange -= VerifyFormatConsistency;
                    card.FormatDropdown.Selected = selectedId;
                    card.OnManualFormatChange += VerifyFormatConsistency;
                }
            }
        }

        private void ResetearRutaGlobal()
        {
            _globalOutputPath = "";
            UpdatePathLabel();
            SavePreferences();
            LogUser("Ruta de salida restablecida a: Mismo origen (Default)");
            
            if (PathLabel != null) {
                var t = CreateTween();
                t.TweenProperty(PathLabel, "modulate", Colors.Green, 0.2f);
                t.TweenProperty(PathLabel, "modulate", Colors.White, 0.5f);
            }
        }

        /// <summary>
        /// Checks if all cards have the same format selected.
        /// If there is a discrepancy, sets master control to '[ Individual ]'.
        /// If uniform, sets master control to the common format.
        /// </summary>
        private void VerifyFormatConsistency()
        {
            if (MasterFormatDropdown == null || SheetListVBox.GetChildCount() == 0) return;

            int firstFormat = -1;
            bool allEqual = true;

            // Verify if all cards share the same format index
            foreach(Node n in SheetListVBox.GetChildren()) {
                if (n is SheetCard card && card.FormatDropdown != null) {
                    if (firstFormat == -1) {
                        firstFormat = card.FormatDropdown.Selected;
                    }
                    else if (card.FormatDropdown.Selected != firstFormat) {
                        allEqual = false;
                        break;
                    }
                }
            }

            // Disable master event to avoid re-propagation
            MasterFormatDropdown.ItemSelected -= OnMasterFormatChanged;

            if (allEqual && firstFormat != -1) {
                // Synchronize Master -> Children: Find visual index corresponding to common format ID
                bool found = false;
                for(int i = 0; i < MasterFormatDropdown.ItemCount; i++) {
                    if (MasterFormatDropdown.GetItemId(i) == firstFormat) {
                        MasterFormatDropdown.Selected = i;
                        found = true;
                        break;
                    }
                }
                // Fallback to Individual if mapping not found (rare)
                if (!found) MasterFormatDropdown.Selected = 0; 
            } else {
                // Desynchronization detected -> Set mode [ Individual ] (ID 99, Index 0)
                MasterFormatDropdown.Selected = 0; 
            }

            // Re-enable master event
            MasterFormatDropdown.ItemSelected += OnMasterFormatChanged;
        }

        /// <summary>
        /// Helper to connect 'Pressed' signals with automatic logging.
        /// </summary>
        private void ConnectButton(Button btn, string actionName, Action action) {
            if (btn != null) {
                btn.Pressed += () => {
                    LogUser($"Click button: {actionName}");
                    action.Invoke();
                };
            }
        }

        // ==========================================================================================
        // DYNAMIC THEME ENGINE
        // ==========================================================================================

        private void OnThemeToggled(bool isDark) {
            SavePreferences();
            ApplyGlobalTheme(isDark);
        }

        /// <summary>
        /// Applies color changes to the theme at runtime by modifying StyleBoxes.
        /// </summary>
        private void ApplyGlobalTheme(bool isDark) {
            var theme = this.Theme; // The ThemeOnyx assigned to the root node
            if (theme == null) return;

            // Color Palette Definition
            Color bgMain    = isDark ? new Color(0.02f, 0.02f, 0.03f) : new Color(0.95f, 0.95f, 0.97f);
            Color bgPanel   = isDark ? new Color(0.05f, 0.05f, 0.06f) : new Color(1.0f, 1.0f, 1.0f);
            Color bgInput   = isDark ? new Color(0.03f, 0.03f, 0.04f) : new Color(0.9f, 0.9f, 0.92f);
            Color border    = isDark ? new Color(0.15f, 0.15f, 0.18f) : new Color(0.85f, 0.85f, 0.9f);
            Color textMain  = isDark ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.1f, 0.1f, 0.15f);
            Color textDim   = isDark ? new Color(0.6f, 0.6f, 0.65f)   : new Color(0.4f, 0.4f, 0.45f);

            // 1. Global Background
            // Note: Adjust node path if hierarchy changed in v2 scene
            var bgRect = GetNodeOrNull<PanelContainer>("BackgroundPanel"); 
            if (bgRect != null) bgRect.SelfModulate = bgMain; 

            // 2. Modify in-memory Theme StyleBoxes
            
            SetStyleBoxColor(theme, "PanelContainer", "panel", bgPanel, border);
            SetStyleBoxColor(theme, "LineEdit", "normal", bgInput, border);
            
            // Buttons (Normal)
            SetStyleBoxColor(theme, "Button", "normal", 
                isDark ? new Color(0.09f, 0.09f, 0.11f) : new Color(1f, 1f, 1f), 
                border);
                
            // Buttons (Hover) - Consistent green accent
            SetStyleBoxColor(theme, "Button", "hover", 
                isDark ? new Color(0.15f, 0.15f, 0.17f) : new Color(0.95f, 0.98f, 0.96f), 
                new Color("10B981"));

            // 3. Force font color update
            theme.SetColor("font_color", "Label", textMain);
            theme.SetColor("font_color", "Button", textMain);
            theme.SetColor("font_color", "LineEdit", textMain);
            theme.SetColor("font_placeholder_color", "LineEdit", textDim);

            LogSys($"Theme applied: {(isDark ? "Dark Onyx" : "Light Day")}");
        }

        private void SetStyleBoxColor(Theme theme, string type, string styleName, Color bg, Color border) {
            if (theme.HasStylebox(styleName, type)) {
                var style = theme.GetStylebox(styleName, type) as StyleBoxFlat;
                if (style != null) {
                    style.BgColor = bg;
                    style.BorderColor = border;
                }
            }
        }

        // ==========================================================================================
        // FORENSIC LOG SYSTEM
        // ==========================================================================================

        public void LogUser(string msg) => LogBase("USER", msg, "5eead4"); // Cyan
        public void LogSys(string msg) => LogBase("SYS", msg, "94a3b8");   // Slate
        public void LogWarn(string msg) => LogBase("WARN", msg, "fbbf24"); // Amber
        public void LogErr(string msg) => LogBase("ERROR", msg, "f87171"); // Red
        
        /// <summary>
        /// Registra eventos de alto volumen exclusivamente en el archivo de disco.
        /// Omitir la UI previene la congelación de la interfaz gráfica durante bucles intensivos.
        /// </summary>
        public void LogTrace(string context, string msg) 
        {
            if (_masterLogFile == null) return;

            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            
            lock (_logLock)
            {
                _masterLogFile.WriteLine($"[{time}] [TRACE] [{context}] {msg}");
            }
        }

        private void LogBase(string category, string msg, string hexColor) 
        {
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            
            // 1. DISK WRITING (Critical Persistence)
            // Lock is used to ensure multiple threads do not corrupt the file.
            if (_masterLogFile != null)
            {
                lock (_logLock)
                {
                    string plainLog = $"[{time}] [{category.PadRight(5)}] {msg}";
                    _masterLogFile.WriteLine(plainLog);
                }
            }
            
            // Print to Godot debug console
            GD.Print($"[{category}] {msg}");

            // 2. UI UPDATE (Deferred)
            // Only the formatted log is sent to the UI for user visualization.
            string formattedEntry = $"[color=#{hexColor}][{time}] [{category}] {msg}[/color]";
            CallDeferred(nameof(ApplyLogUpdate), formattedEntry);
        }

        private void ApplyLogUpdate(string formattedEntry) {
            _systemLogs.Add(formattedEntry);
            if (_systemLogs.Count > 300) _systemLogs.RemoveAt(0);

            if (!_isViewingIndividualLogs && LogRichText != null) {
                LogRichText.Text = string.Join("\n", _systemLogs);
            }
        }

        // ==========================================================================================
        // FILE MANAGEMENT (Drag & Drop and Selection)
        // ==========================================================================================

        private async void OnFilesDropped(string[] paths)
        {
            if (paths.Length == 0) return;
            
            LogSys($"Drag&Drop detected. Count: {paths.Length} items.");
            ToggleLoading(true, "Analyzing files..."); 

            var validFiles = new List<string>();
            foreach(var p in paths) {
                string ext = Path.GetExtension(p).ToLower();
                if(_validExtensions.Contains(ext)) {
                    validFiles.Add(p);
                } else {
                    LogWarn($"Ignored file (Unsupported extension): {Path.GetFileName(p)}");
                }
            }

            if (validFiles.Count == 0) {
                LogWarn("No valid files in selection.");
                ToggleLoading(false);
                return;
            }

            // 1. INSTA-CARD (Immediate Visual Creation)
            var newCards = new List<SheetCard>();
            foreach (var path in validFiles) {
                var card = SheetCardPrefab.Instantiate<SheetCard>();
                
                // Subscription to manual change event for reverse synchronization
                card.OnManualFormatChange += VerifyFormatConsistency;
                
                SheetListVBox.AddChild(card);
                
                var dummyData = new SheetData { SourceFileName = path, SheetName = "Analyzing..." };
                card.ConfigureSheet(dummyData, this); 
                
                // Synchronize initial format with Master if not in individual mode
                if (MasterFormatDropdown != null) {
                    int masterId = MasterFormatDropdown.GetItemId(MasterFormatDropdown.Selected);
                    if (masterId != 99) {
                        card.FormatDropdown.Selected = masterId;
                    }
                }

                card.SetLoading(true);
                card.AnimateEntry(0.1f);
                
                newCards.Add(card);
            }
            UpdateCounter();

            // 2. ASYNCHRONOUS PROCESSING
            for (int i = 0; i < newCards.Count; i++) {
                var card = newCards[i];
                var path = validFiles[i];
                
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // Allow UI refresh

                var results = await Task.Run(() => AnalyzeFiles(new[] { path }));
                
                if (results.Count > 0) {
                    // SUCCESS
                    card.ConfigureSheet(results[0], this);
                    card.GenerateUIColumns();
                    card.LiveDebug();
                    card.SetLoading(false); 
                    card.AddLog("Structure analyzed successfully.", false);
                    LogSys($"Analysis completed for: {Path.GetFileName(path)}");
                } else {
                    // FAILURE
                    card.SetStatus(3); 
                    card.SetLoading(false);
                    card.AddLog("Critical failure: Could not read structure.", true);
                    LogErr($"Analysis failed in: {Path.GetFileName(path)}");
                }
            }
            ToggleLoading(false);
        }

        private void OnExportButtonPressed() {
            // Redirect to async export method
            ExecuteExportProcess();
        }

        // ==========================================================================================
        // BUSINESS LOGIC: File Analysis
        // ==========================================================================================

        private List<SheetData> AnalyzeFiles(string[] paths)
        {
            var results = new List<SheetData>();
            foreach (var path in paths)
            {
                try {
                    string ext = Path.GetExtension(path).ToLower();

                    // === CASE 1: ODS (OpenDocument Spreadsheet) ===
                    if (ext == ".ods")
                    {
                        using (var fs = System.IO.File.Open(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite))
                        using (var ods = new OdsReader(fs)) 
                        {
                            if (ods.MoveToNextSheet()) { 
                                do {
                                    var sheet = ProcessGenericReader(ods, path, ods.SheetName);
                                    if (sheet != null) results.Add(sheet);
                                } while (ods.MoveToNextSheet()); 
                            }
                        }
                    }
                    // === CASE 2: EXCEL (XLS/XLSX via NPOI) ===
                    else if (ext == ".xlsx" || ext == ".xls")
                    {
                        using (var fs = System.IO.File.Open(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite))
                        using (var npoi = new ExcelReaderNPOI(fs, ext)) 
                        {
                            while (npoi.MoveToNextSheet()) {
                                var sheet = ProcessGenericReader(npoi, path, npoi.SheetName);
                                if (sheet != null) results.Add(sheet);
                            }
                        }
                    }
                    // === CASE 3: FLAT TEXT (CSV/TXT) ===
                    else if (ext == ".csv" || ext == ".txt")
                    {
                        var sheet = ReadFlatText(path);
                        if (sheet != null) results.Add(sheet);
                    }
                    // === CASE 4: DBF (Legacy) ===
                    else if (ext == ".dbf")
                    {
                        var options = new DbfDataReaderOptions { Encoding = Encoding.GetEncoding(1252) };
                        using (var dbfReader = new DbfDataReader.DbfDataReader(path, options)) 
                        {
                            var sheet = new SheetData {
                                SourceFileName = Path.GetFileName(path),
                                SourceFullPath = path,
                                SheetName = "DBFTable"
                            };
                            
                            var headers = new List<string>();
                            for(int i=0; i<dbfReader.FieldCount; i++) headers.Add(dbfReader.GetName(i));

                            int rowCount = 0;
                            while(dbfReader.Read()) {
                                var row = new object[dbfReader.FieldCount];
                                for(int i=0; i<dbfReader.FieldCount; i++) row[i] = dbfReader.GetValue(i);
                                
                                sheet.RawRows.Add(row);
                                if (rowCount < 50) sheet.DebugSample.Add(row);
                                rowCount++;
                            }
                            sheet.Structure = InferColumnTypes(headers, sheet.RawRows);
                            results.Add(sheet);
                        }
                    }

                } catch (Exception ex) { 
                    LogErr($"Error reading {path}: {ex.Message}");
                }
            }
            return results;
        }

        private SheetData ProcessGenericReader(dynamic reader, string path, string sheetName)
        {
            if (!reader.ReadRow()) return null; 

            var sheet = new SheetData {
                SourceFileName = Path.GetFileName(path),
                SourceFullPath = path,
                SheetName = sheetName
            };

            var headers = new List<string>();
            for(int i=0; i<reader.FieldCount; i++) headers.Add(reader.GetValue(i)?.ToString() ?? $"C{i}");

            int rowCount = 0;
            while(reader.ReadRow()) {
                var row = new object[reader.FieldCount];
                for(int i=0; i<reader.FieldCount; i++) row[i] = reader.GetValue(i);
                
                sheet.RawRows.Add(row);
                if (rowCount < 50) sheet.DebugSample.Add(row);
                rowCount++;
            }
            
            sheet.Structure = InferColumnTypes(headers, sheet.RawRows);
            return sheet;
        }

        private SheetData ReadFlatText(string path)
        {
            var sheet = new SheetData {
                SourceFileName = Path.GetFileName(path),
                SourceFullPath = path,
                SheetName = "Text"
            };
            
            List<string> detectedHeaders = null;
            var regexCSV = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

            try {
                using (var fs = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.GetEncoding(1252))) 
                {
                    string line;
                    int counter = 0;

                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        string[] parts;
                        if (path.ToLower().EndsWith(".csv")) {
                            parts = regexCSV.Split(line);
                            for(int i=0; i<parts.Length; i++) parts[i] = parts[i].Trim('"');
                        } else {
                            parts = line.Split(new[]{'\t', ';'});
                        }

                        if (detectedHeaders == null) {
                            detectedHeaders = new List<string>(parts);
                        }
                        else {
                            sheet.RawRows.Add(parts);
                            if (counter < 50) sheet.DebugSample.Add(parts);
                            counter++;
                        }
                    }
                }
            } 
            catch (IOException ex) {
                LogErr($"IO Error {path}: {ex.Message}");
                return null;
            }

            if (sheet.RawRows.Count > 0) {
                 if (detectedHeaders == null || detectedHeaders.Count == 0) {
                     detectedHeaders = new List<string>();
                     int maxCols = sheet.RawRows[0].Length;
                     for(int i=0; i<maxCols; i++) detectedHeaders.Add("COL_"+(i+1));
                 }
                 sheet.Structure = InferColumnTypes(detectedHeaders, sheet.RawRows);
            }

            return sheet;
        }

        // ==========================================================================================
        // BUSINESS LOGIC: Export and Forging ("The Hopper")
        // ==========================================================================================

        private async void ExecuteExportProcess()
        {
            if(ProcessFilesBtn != null) ProcessFilesBtn.Disabled = true;
            
            var tickets = GenerateTicketsFromUI();
            if (tickets.Count == 0) { 
                LogWarn("Export attempt aborted: No valid or enabled files.");
                if(ProcessFilesBtn != null) ProcessFilesBtn.Disabled = false;
                return; 
            }

            LogSys($"=== STARTING EXPORT PROCESS: {tickets.Count} files ===");
            if(GlobalProgressBar != null) GlobalProgressBar.Value = 0;
            ToggleLoading(true, "Processing export...");

            // Collection to track unique destinations and open folders upon completion
            var destinationFolders = new HashSet<string>();

            await Task.Run(() => {
                int total = tickets.Count;
                for(int i = 0; i < total; i++)
                {
                    var ticket = tickets[i];
                    
                    // UI Status Update (Thread-Safe via CallDeferred)
                    ticket.UICard.CallDeferred("SetStatus", 1); 
                    
                    // Execution of export engine ("The Hopper")
                    string result = ExecuteDataExport(ticket);
                    
                    // Post-process updates
                    if (result == "OK") {
                        ticket.UICard.CallDeferred("SetStatus", 2); 
                        ticket.UICard.CallDeferred("PopulatePreviewResults", ticket.FinalPathUsed);
                        
                        // Register successful destination folder
                        string dir = Path.GetDirectoryName(ticket.FinalPathUsed);
                        if (!string.IsNullOrEmpty(dir)) destinationFolders.Add(dir);
                    } 
                    else if (result == "WARN") {
                        ticket.UICard.CallDeferred("SetStatus", 4);
                        ticket.UICard.CallDeferred("PopulatePreviewResults", ticket.FinalPathUsed);
                        
                        string dir = Path.GetDirectoryName(ticket.FinalPathUsed);
                        if (!string.IsNullOrEmpty(dir)) destinationFolders.Add(dir);
                    }
                    else {
                        ticket.UICard.CallDeferred("SetStatus", 3); 
                    }

                    // Global progress update
                    float progress = ((float)(i + 1) / total) * 100;
                    if(GlobalProgressBar != null) GlobalProgressBar.CallDeferred("set_value", progress);
                }
            });

            LogSys("=== PROCESS FINISHED ===");
            ToggleLoading(false);
            if(ProcessFilesBtn != null) ProcessFilesBtn.Disabled = false;
            
            // Folder opening logic
            if (destinationFolders.Count > 0) {
                LogSys($"Detected {destinationFolders.Count} unique output destinations.");
                foreach (var folder in destinationFolders) {
                    if (Directory.Exists(folder)) {
                        LogSys($"Opening output directory: {folder}");
                        OS.ShellOpen(folder);
                    }
                }
                
                if (destinationFolders.Count > 1) {
                    LogUser("Note: Files have been distributed across multiple folders.");
                }
            }
        }

        private List<ForgeTicket> GenerateTicketsFromUI()
        {
            var list = new List<ForgeTicket>();
            bool isMergeMode = (MergeSwitch != null && MergeSwitch.ButtonPressed);

            var activeCards = new List<SheetCard>();
            foreach (Node node in SheetListVBox.GetChildren())
            {
                if (node is SheetCard card && card.EnableCheckbox.ButtonPressed)
                    activeCards.Add(card);
            }

            if (activeCards.Count == 0) return list;

            // --- MERGE MODE LOGIC ---
            if (isMergeMode)
            {
                var masterCard = activeCards[0];
                var masterTicket = new ForgeTicket();
                
                masterTicket.UICard = masterCard;
                masterTicket.OutputFormat = masterCard.FormatDropdown.Selected;
                
                // 1. Path Config
                string mergeName = (MergeFilenameInput != null && !string.IsNullOrWhiteSpace(MergeFilenameInput.Text)) 
                                    ? MergeFilenameInput.Text.Trim() 
                                    : "Fusion_Master";

                string desiredExt = masterTicket.OutputFormat switch {
                    1 => ".xlsx",
                    2 => ".csv",
                    _ => ".dbf"
                };

                if (!mergeName.ToLower().EndsWith(desiredExt))
                    mergeName = Path.ChangeExtension(mergeName, desiredExt);

                string destPath = masterCard.GetEffectiveOutputPath(_globalOutputPath);
                if (string.IsNullOrEmpty(destPath))
                    destPath = Path.GetDirectoryName(masterCard.AssociatedData.SourceFullPath);

                masterTicket.BasePath = Path.Combine(destPath, mergeName);

                // 2. Schema Definition with Intelligent Truncation
                masterTicket.ColumnDefinitions = new List<DetectedColumn>();
                var masterUICols = masterCard.GetUIDefinitions();
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var colUI in masterUICols)
                {
                    string baseName = Regex.Replace(colUI.OriginalName.ToUpperInvariant(), @"[^A-Z0-9_]", "_");
                    if (char.IsDigit(baseName[0])) baseName = "_" + baseName;
                    
                    // Patch: Smart Truncate strategy.
                    // Instead of simple substring(0, 10), we preserve the start and end of the string
                    // to avoid collisions like "DEPARTAMENTO_A" -> "DEPARTAMEN" vs "DEPARTAMENTO_B" -> "DEPARTAMEN".
                    string finalName;
                    if (baseName.Length > 10) 
                    {
                         // Strategy: First 6 chars + Last 3 chars (e.g. DEPART_TO_A)
                         // 10 chars max: 6 prefix + 1 connector + 3 suffix? Or just 7 prefix + 3 suffix.
                         // DBF limit is strict. Let's use 6 + 4 split if needed or just 10.
                         // Better Logic: First 6 + Last 4 if possible, but strict limit is 10.
                         // Implementation: First 5 chars + Last 4 chars = 9 chars + safety.
                         finalName = baseName.Substring(0, 5) + baseName.Substring(baseName.Length - 4);
                         // Note: If original was > 10, this ensures "DEPAR...TO_A" distinctiveness.
                         // Re-sanitize just in case
                         finalName = finalName.Substring(0, Math.Min(10, finalName.Length));
                    }
                    else 
                    {
                        finalName = baseName;
                    }

                    // Collision Resolution
                    int c = 1;
                    string temp = finalName;
                    while (usedNames.Contains(temp))
                    {
                        string suffix = $"_{c++}";
                        int space = 10 - suffix.Length;
                        temp = (finalName.Length > space ? finalName.Substring(0, space) : finalName) + suffix;
                    }

                    usedNames.Add(temp);
                    colUI.OriginalName = temp;
                    masterTicket.ColumnDefinitions.Add(colUI);
                }

                // 3. Data Aggregation
                masterTicket.DataReadyForDBF = new List<object[]>();
                
                foreach (var card in activeCards)
                {
                    var currentIndices = card.GetActiveIndices();
                    
                    foreach (var rawRow in card.AssociatedData.RawRows)
                    {
                        var cleanRow = new object[masterTicket.ColumnDefinitions.Count];
                        
                        for (int i = 0; i < masterTicket.ColumnDefinitions.Count; i++)
                        {
                            int sourceIdx = (i < currentIndices.Count) ? currentIndices[i] : -1;
                            object value = (sourceIdx != -1 && sourceIdx < rawRow.Length) ? rawRow[sourceIdx] : null;

                            var res = SanitizeDataWithReport(value, masterTicket.ColumnDefinitions[i].SuggestedType);
                            cleanRow[i] = res.SanitizedValue;
                        }
                        masterTicket.DataReadyForDBF.Add(cleanRow);
                    }
                }
                list.Add(masterTicket);
            }
            // --- INDIVIDUAL MODE LOGIC ---
            else
            {
                foreach (var card in activeCards)
                {
                    var ticket = new ForgeTicket();
                    ticket.UICard = card;
                    ticket.OutputFormat = card.FormatDropdown.Selected;

                    string userName = card.OutputFilenameInput.Text.Trim();
                    if (string.IsNullOrEmpty(userName)) userName = "Untitled";

                    string desiredExt = ticket.OutputFormat switch {
                        1 => ".xlsx",
                        2 => ".csv",
                        _ => ".dbf"
                    };

                    if (!userName.ToLower().EndsWith(desiredExt))
                        userName = Path.ChangeExtension(userName, desiredExt);

                    string destPath = card.GetEffectiveOutputPath(_globalOutputPath);
                    if (string.IsNullOrEmpty(destPath))
                        destPath = Path.GetDirectoryName(card.AssociatedData.SourceFullPath);

                    ticket.BasePath = Path.Combine(destPath, userName);

                    ticket.ColumnDefinitions = new List<DetectedColumn>();
                    var uiColumns = card.GetUIDefinitions();
                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var colUI in uiColumns)
                    {
                        string baseName = Regex.Replace(colUI.OriginalName.ToUpperInvariant(), @"[^A-Z0-9_]", "_");
                        if (char.IsDigit(baseName[0])) baseName = "_" + baseName;
                        
                        // Apply same Smart Truncate logic for consistency even in individual mode
                        string finalName;
                        if (baseName.Length > 10) 
                            finalName = baseName.Substring(0, 5) + baseName.Substring(baseName.Length - 4);
                        else 
                            finalName = baseName;
                        
                        finalName = finalName.Substring(0, Math.Min(10, finalName.Length));

                        int c = 1;
                        string temp = finalName;
                        while (usedNames.Contains(temp))
                        {
                            string suffix = $"_{c++}";
                            int space = 10 - suffix.Length;
                            temp = (finalName.Length > space ? finalName.Substring(0, space) : finalName) + suffix;
                        }

                        usedNames.Add(temp);
                        colUI.OriginalName = temp;
                        ticket.ColumnDefinitions.Add(colUI);
                    }

                    var activeIndices = card.GetActiveIndices();
                    ticket.DataReadyForDBF = new List<object[]>();

                    foreach (var rawRow in card.AssociatedData.RawRows)
                    {
                        var cleanRow = new object[ticket.ColumnDefinitions.Count];
                        for (int i = 0; i < ticket.ColumnDefinitions.Count; i++)
                        {
                            int idx = (i < activeIndices.Count) ? activeIndices[i] : -1;
                            object value = (idx != -1 && idx < rawRow.Length) ? rawRow[idx] : null;

                            var res = SanitizeDataWithReport(value, ticket.ColumnDefinitions[i].SuggestedType);
                            cleanRow[i] = res.SanitizedValue;
                        }
                        ticket.DataReadyForDBF.Add(cleanRow);
                    }
                    list.Add(ticket);
                }
            }

            return list;
        }

        private string ExecuteDataExport(ForgeTicket ticket)
        {
            try 
            {
                // Context identifier for the physical log
                string contextID = ticket.UICard?.AssociatedData?.SourceFileName ?? "UNKNOWN_SHEET";

                // --- PHASE 1: INITIALIZATION AND CONTEXT ---
                ticket.UICard.CallDeferred("AddLog", "==================================================", false);
                ticket.UICard.CallDeferred("AddLog", $" STARTING DATA AUTOPSY | {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}", false);
                ticket.UICard.CallDeferred("AddLog", "==================================================", false);
                
                ticket.FinalPathUsed = GetSafeWritePath(ticket.BasePath);
                ticket.UICard.CallDeferred("AddLog", $"[IO] File descriptor assigned: {ticket.FinalPathUsed}", false);
                
                bool hadWarnings = false;
                int totalRows = ticket.DataReadyForDBF.Count;
                int totalCols = ticket.ColumnDefinitions.Count;

                // --- PHASE 2: EXPORT ENGINE SELECTION ---

                // === ENGINE 1: DBF (FastDBF - xBase Legacy Support) ===
                if (ticket.OutputFormat == 0) 
                {
                    ticket.UICard.CallDeferred("AddLog", "[SYS] Initializing FastDBF driver (Encoding: 1252)...", false);
                    
                    using (var fs = System.IO.File.Open(ticket.FinalPathUsed, FileMode.Create, System.IO.FileAccess.Write)) 
                    {
                        var dbf = new FastDBF.DbfFile(Encoding.GetEncoding(1252));
                        dbf.Open(fs);

                        // A. SCHEMA DEFINITION
                        ticket.UICard.CallDeferred("AddLog", $"[SCHEMA] Defining structure for {totalCols} columns:", false);
                        
                        foreach(var col in ticket.ColumnDefinitions) {
                            var t = FastDBF.DbfColumn.DbfColumnType.Character;
                            string rawType = "CHAR";
                            
                            if(col.SuggestedType==1) { t=FastDBF.DbfColumn.DbfColumnType.Number; rawType="NUMERIC"; }
                            if(col.SuggestedType==2) { t=FastDBF.DbfColumn.DbfColumnType.Integer; rawType="INTEGER"; }
                            if(col.SuggestedType==3) { t=FastDBF.DbfColumn.DbfColumnType.Date; rawType="DATE"; }
                            if(col.SuggestedType==4) { t=FastDBF.DbfColumn.DbfColumnType.Boolean; rawType="LOGICAL"; }
                            if(col.SuggestedType==5) { t=FastDBF.DbfColumn.DbfColumnType.Character; rawType="CHAR(Time)"; }

                            if (col.SuggestedType == 1 && col.Decimals >= col.Length - 1) 
                                    col.Decimals = Math.Max(0, col.Length - 2);

                            string logStruct = $"   └─ [DEF] '{col.OriginalName}' >> Type:{rawType} | Len:{col.Length} | Dec:{col.Decimals}";
                            ticket.UICard.CallDeferred("AddLog", logStruct, false);

                            dbf.Header.AddColumn(new FastDBF.DbfColumn(col.OriginalName, t, col.Length, col.Decimals));
                        }

                        // B. DATA DUMP
                        ticket.UICard.CallDeferred("AddLog", $"[DATA] Starting serialization of {totalRows} records...", false);
                        
                        int rowIdx = 1;
                        foreach(var rowObj in ticket.DataReadyForDBF) {
                            var rec = new FastDBF.DbfRecord(dbf.Header);
                            rec.AllowIntegerTruncate = true; 
                            
                            for(int i=0; i<rowObj.Length; i++) {
                                var defCol = ticket.ColumnDefinitions[i];
                                object rawVal = rowObj[i];
                                string strRaw = rawVal?.ToString() ?? "NULL";
                                string originType = rawVal?.GetType().Name ?? "Void";

                                var res = SanitizeDataWithReport(rawVal, defCol.SuggestedType);
                                string strFinal = res.SanitizedValue?.ToString() ?? "";

                                // SURGICAL TRACE LOGGING
                                // Captures input type, output value, and specific coordinates.
                                string traceMsg = $"R{rowIdx:D4}:C{i:D3} [{defCol.OriginalName}] | TypeID:{defCol.SuggestedType} | IN: '{strRaw}' ({originType}) >>> OUT: '{strFinal}'";

                                if (res.HasLoss) {
                                    string msgWarn = $"[WARN] {traceMsg} | MUTATION DETECTED";
                                    ticket.UICard.CallDeferred("AddLog", msgWarn, true);
                                    hadWarnings = true;
                                } 
                                else {
                                    // Direct-to-disk Trace
                                    LogTrace(contextID, traceMsg);
                                }

                                if (defCol.SuggestedType == 3 && res.SanitizedValue is DateTime dt)
                                    rec[i] = dt.ToString("yyyy-MM-dd");
                                else
                                    rec[i] = strFinal;
                            }
                            dbf.Write(rec);
                            rowIdx++;
                        }
                        ticket.UICard.CallDeferred("AddLog", "[IO] Finishing writing and closing DBF stream.", false);
                        dbf.Close();
                    }
                }
                // === ENGINE 2: EXCEL (NPOI - Office Open XML) ===
                else if (ticket.OutputFormat == 1)
                {
                    ticket.UICard.CallDeferred("AddLog", "[SYS] Initializing NPOI engine (XLSX)...", false);
                    
                    IWorkbook workbook = new XSSFWorkbook(); 
                    ISheet sheet = workbook.CreateSheet("Forensic_Export");
                    ticket.UICard.CallDeferred("AddLog", "[NPOI] Workbook and Sheet created in memory.", false);

                    var dateStyle = workbook.CreateCellStyle();
                    dateStyle.DataFormat = workbook.CreateDataFormat().GetFormat("yyyy-mm-dd");
                    var timeStyle = workbook.CreateCellStyle();
                    timeStyle.DataFormat = workbook.CreateDataFormat().GetFormat("[h]:mm:ss");

                    IRow headerRow = sheet.CreateRow(0);
                    for (int i = 0; i < totalCols; i++) {
                        string colName = ticket.ColumnDefinitions[i].OriginalName;
                        headerRow.CreateCell(i).SetCellValue(colName);
                        ticket.UICard.CallDeferred("AddLog", $"[SCHEMA] Column {i}: {colName}", false);
                    }

                    ticket.UICard.CallDeferred("AddLog", "[DATA] Iterating object matrix for Excel conversion...", false);
                    
                    int rowIdx = 1;
                    foreach (var rowObj in ticket.DataReadyForDBF)
                    {
                        IRow row = sheet.CreateRow(rowIdx);
                        for (int c = 0; c < rowObj.Length; c++)
                        {
                            var defCol = ticket.ColumnDefinitions[c];
                            object rawVal = rowObj[c];
                            string strRaw = rawVal?.ToString() ?? "NULL";
                            string originType = rawVal?.GetType().Name ?? "Void";
                            
                            var res = SanitizeDataWithReport(rawVal, defCol.SuggestedType);
                            string strFinal = res.SanitizedValue?.ToString() ?? "";
                            
                            // SURGICAL TRACE LOGGING
                            string traceMsg = $"R{rowIdx:D4}:C{c:D3} [{defCol.OriginalName}] | TypeID:{defCol.SuggestedType} | IN: '{strRaw}' ({originType}) >>> OUT: '{strFinal}'";

                            if (res.HasLoss) {
                                string msgWarn = $"[WARN] {traceMsg} | MUTATION DETECTED";
                                ticket.UICard.CallDeferred("AddLog", msgWarn, true);
                                hadWarnings = true;
                            } else {
                                LogTrace(contextID, traceMsg);
                            }

                            var value = res.SanitizedValue; 
                            var cell = row.CreateCell(c);
                            
                            if (value == null) continue;
                            
                            string sVal = value.ToString();
                            int type = defCol.SuggestedType;

                            if (type == 1 || type == 2) {
                                if (double.TryParse(sVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                                    cell.SetCellValue(d);
                                else 
                                    cell.SetCellValue(sVal);
                            }
                            else if (type == 3 && value is DateTime dt) {
                                cell.SetCellValue(dt);
                                cell.CellStyle = dateStyle;
                            }
                            else if (type == 5) {
                                if (CalculateDayFraction(sVal, out double excelTime)) {
                                    cell.SetCellValue(excelTime);
                                    cell.CellStyle = timeStyle;
                                } else {
                                    cell.SetCellValue(sVal);
                                }
                            }
                            else {
                                cell.SetCellValue(sVal);
                            }
                        }
                        rowIdx++;
                    }
                    
                    ticket.UICard.CallDeferred("AddLog", "[IO] Dumping XLSX FileStream to disk...", false);
                    using (var fs = new System.IO.FileStream(ticket.FinalPathUsed, FileMode.Create, System.IO.FileAccess.Write)) {
                        workbook.Write(fs);
                    }
                    workbook.Close();
                }
                // === ENGINE 3: CSV (Flat Text / RFC 4180) ===
                else if (ticket.OutputFormat == 2)
                {
                    ticket.UICard.CallDeferred("AddLog", "[SYS] Initializing StreamWriter (UTF-8)...", false);
                    
                    using (var sw = new StreamWriter(ticket.FinalPathUsed, false, Encoding.UTF8)) {
                        var headers = ticket.ColumnDefinitions.Select(c => EscapeCSV(c.OriginalName));
                        string headerLine = string.Join(",", headers);
                        sw.WriteLine(headerLine);
                        ticket.UICard.CallDeferred("AddLog", $"[SCHEMA] Header written: {headerLine}", false);

                        int csvRow = 1;
                        foreach(var rowObj in ticket.DataReadyForDBF) {
                            var cells = new List<string>();
                            
                            for(int i=0; i<rowObj.Length; i++) {
                                var defCol = ticket.ColumnDefinitions[i];
                                object rawVal = rowObj[i];
                                string strRaw = rawVal?.ToString() ?? "NULL";
                                string originType = rawVal?.GetType().Name ?? "Void";

                                var res = SanitizeDataWithReport(rowObj[i], defCol.SuggestedType);
                                string strFinal = res.SanitizedValue?.ToString() ?? "";
                                
                                // SURGICAL TRACE LOGGING
                                string traceMsg = $"Line {csvRow:D4} | Col {i:D3} [{defCol.OriginalName}] | TypeID:{defCol.SuggestedType} | IN: '{strRaw}' ({originType}) >>> OUT: '{strFinal}'";

                                if (res.HasLoss) {
                                    string msgWarn = $"[WARN] {traceMsg} | MUTATION DETECTED";
                                    ticket.UICard.CallDeferred("AddLog", msgWarn, true);
                                    hadWarnings = true;
                                } else {
                                    LogTrace(contextID, traceMsg);
                                }
                                
                                var val = res.SanitizedValue;
                                if (defCol.SuggestedType == 3 && val is DateTime dt)
                                    cells.Add(dt.ToString("yyyy-MM-dd"));
                                else
                                    cells.Add(EscapeCSV(val?.ToString() ?? ""));
                            }
                            sw.WriteLine(string.Join(",", cells));
                            csvRow++;
                        }
                    }
                    ticket.UICard.CallDeferred("AddLog", "[IO] CSV stream closed successfully.", false);
                }
                
                // --- PHASE 3: CONCLUSION AND STATISTICS ---
                ticket.UICard.CallDeferred("AddLog", "--------------------------------------------------", false);
                ticket.UICard.CallDeferred("AddLog", $" END TIME: {DateTime.Now:HH:mm:ss.fff}", false);
                
                if (hadWarnings) {
                    ticket.UICard.CallDeferred("AddLog", " [RESULT] FINISHED WITH RISKS (Check [WARN] tags)", true);
                    return "WARN";
                }
                
                ticket.UICard.CallDeferred("AddLog", " [RESULT] OPERATION SUCCESSFUL (100% Integrity)", false);
                return "OK";

            } catch (Exception e) { 
                string errorContext = $"[FATAL CRASH] Unhandled Runtime Exception:\n{e.GetType().Name}: {e.Message}\nStack: {e.StackTrace}";
                ticket.UICard.CallDeferred("AddLog", errorContext, true);
                return e.Message; 
            }
        }

        // ==========================================================================================
        // UTILITY AND SUPPORT METHODS
        // ==========================================================================================

        private bool CalculateDayFraction(string raw, out double result)
        {
            result = 0;
            try {
                var parts = raw.Trim().Split(':');
                double h=0, m=0, s=0;
                if(parts.Length > 0) double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out h);
                if(parts.Length > 1) double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out m);
                if(parts.Length > 2) double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out s);
                
                result = (h + (m/60.0) + (s/3600.0)) / 24.0;
                return true;
            } catch { return false; }
        }

        private string EscapeCSV(string s) {
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n")) {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        public (object SanitizedValue, bool HasLoss) SanitizeDataWithReport(object raw, int type) 
        {
            // Verificación temprana de nulos o DBNull para evitar excepciones de referencia nula
            if (raw == null || raw is DBNull) 
            {
                // Retorno de valores por defecto seguros según el tipo de dato esperado
                if (type == 1 || type == 2) return ("0", false);          // Numérico / Entero
                if (type == 4) return ("F", false);                       // Lógico
                if (type == 3) return (new DateTime(1900, 1, 1), false);  // Fecha base mínima
                return ("", false);                                       // Carácter / Tiempo
            }

            string sVal = raw.ToString().Trim();

            // --- CASE 5: TIME (Tiempo/Duración) ---
            if (type == 5) 
            {
                if (string.IsNullOrEmpty(sVal)) return ("", false);
                
                // Si el objeto origen ya es DateTime, formateamos directamente
                if (raw is DateTime dt) return (dt.ToString("HH:mm:ss"), false);

                // Intento directo por Expresión Regular para formatos HH:MM:SS
                var match = Regex.Match(sVal, @"^-?\d+:\d{2}(:\d{2})?");
                if (match.Success) return (match.Value, false);
                
                // Intento de conversión desde fracción decimal de Excel (ej. 0.5 = 12:00 PM)
                // Se normaliza coma a punto para asegurar compatibilidad con InvariantCulture
                string sValNorm = sVal.Replace(",", ".");
                if (double.TryParse(sValNorm, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) 
                {
                    try 
                    {
                        TimeSpan ts = TimeSpan.FromDays(d);
                        string sign = ts.TotalSeconds < 0 ? "-" : "";
                        ts = ts.Duration(); 
                        // Construcción manual del string para evitar inconsistencias de cultura local
                        return ($"{sign}{(int)Math.Floor(ts.TotalHours)}:{ts.Minutes:00}:{ts.Seconds:00}", false);
                    } 
                    catch 
                    { 
                        return ("", true); // Retorno de error por desbordamiento o NaN
                    }
                }
                // Fallback: truncado de seguridad si no se pudo interpretar
                return (sVal.Length > 20 ? sVal.Substring(0, 20) : sVal, true); 
            }

            // --- CASE 3: DATE (Fecha) ---
            if (type == 3) 
            { 
                if (string.IsNullOrEmpty(sVal) || sVal.Contains("0000")) return (new DateTime(1900, 1, 1), false); 
                
                // Manejo directo si el objeto subyacente ya es DateTime
                if (raw is DateTime dt) 
                {
                    // Normalización para fechas previas a 1900 que pueden causar problemas en DBF antiguos
                    if (dt.Year < 1900) return (dt.ToString("yyyy-MM-dd"), true); 
                    return (dt, false);
                }

                // Estrategia de parseo doble: Intentar primero formato ISO/Invariante, luego Cultura Local
                DateTime dtP;
                bool parsed = DateTime.TryParse(sVal, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dtP);
                
                if (!parsed) {
                    parsed = DateTime.TryParse(sVal, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out dtP);
                }

                if (parsed) 
                {
                    if (dtP.Year < 1900) return (dtP.ToString("yyyy-MM-dd"), true); 
                    return (dtP, false);
                }
                
                // Fallback: Soporte para "Serial Date" numérico de Excel
                if (double.TryParse(sVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double serialDate))
                {
                    try {
                        var dtFromSerial = DateTime.FromOADate(serialDate);
                        if (dtFromSerial.Year < 1900) return (dtFromSerial.ToString("yyyy-MM-dd"), true);
                        return (dtFromSerial, false);
                    } catch { /* Ignorar error y proceder al retorno por defecto */ }
                }

                return (new DateTime(1900, 1, 1), true); // Dato irrecuperable, se asume fecha base
            }
            
            // --- CASE 1 & 2: NUMERIC / INTEGER ---
            if (type == 1 || type == 2) 
            { 
                if (string.IsNullOrEmpty(sVal)) return ("0", false);
                
                // Limpieza agresiva: eliminar todo excepto dígitos, puntos, comas, signo menos y notación científica
                string cleanNum = Regex.Replace(sVal, @"[^\d\.,-E]", ""); 

                // Algoritmo heurístico para normalización de separadores (Miles vs Decimales)
                // Detecta ambigüedad cuando existen ambos separadores (ej: 1,200.50 vs 1.200,50)
                int lastComma = cleanNum.LastIndexOf(',');
                int lastDot = cleanNum.LastIndexOf('.');

                if (lastComma != -1 && lastDot != -1)
                {
                    // Si existen ambos, el que esté más a la derecha es el decimal
                    if (lastComma > lastDot) 
                    {
                        // Formato EU (1.234,56) -> Eliminar puntos, cambiar coma a punto
                        cleanNum = cleanNum.Replace(".", "").Replace(",", ".");
                    }
                    else 
                    {
                        // Formato US (1,234.56) -> Eliminar comas
                        cleanNum = cleanNum.Replace(",", "");
                    }
                }
                else if (lastComma != -1)
                {
                    // Solo hay comas. Si hay más de una, son miles. Si hay una, asumimos decimal 
                    // a menos que sea un entero formateado como 1,000
                    // Estrategia segura: Reemplazar coma por punto para parseo Invariante
                    cleanNum = cleanNum.Replace(",", ".");
                }
                // Si solo hay puntos, se asume formato Invariante o miles US (se intentará parsear directo)

                if (double.TryParse(cleanNum, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedNum))
                {
                    // Verificación específica para Enteros
                    if (type == 2) return ((long)parsedNum, sVal != ((long)parsedNum).ToString()); 
                    return (parsedNum, false);
                }
                return ("0", true); // Fallo crítico de conversión
            }

            // --- CASE 4: LOGICAL (Booleano) ---
            if (type == 4) 
            {
                string up = sVal.ToUpperInvariant();
                bool isTrue = (up == "TRUE" || up == "T" || up == "Y" || up == "S" || up == "1" || up == "SI" || up == "YES");
                return (isTrue ? "T" : "F", false);
            }

            // --- CASE 0: CHARACTER (Texto) ---
            // Retorno directo del valor limpio
            return (sVal, false);
        }
        
        private string GetSafeWritePath(string basePath) {
            if (!IsFileLocked(new FileInfo(basePath))) return basePath;
            
            string folder = Path.GetDirectoryName(basePath);
            string name = Path.GetFileNameWithoutExtension(basePath);
            string ext = Path.GetExtension(basePath);
            int i = 1;
            while (true) {
                string newPath = Path.Combine(folder, $"{name}_{i}{ext}");
                if (!IsFileLocked(new FileInfo(newPath))) return newPath;
                i++;
            }
        }
        
        private bool IsFileLocked(FileInfo file) {
            if (!file.Exists) return false;
            try { 
                using (FileStream stream = file.Open(FileMode.Open, System.IO.FileAccess.Read, FileShare.None)) { stream.Close(); } 
            } catch (IOException) { return true; }
            return false;
        }
        
        private List<DetectedColumn> InferColumnTypes(List<string> headers, List<object[]> rows) 
        {
            var columns = new List<DetectedColumn>();
            
            // Regex patterns
            var regexTime = new Regex(@"^-?\d+:\d{2}(:\d{2})?$", RegexOptions.Compiled);
            
            // Boolean hashset
            var boolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "TRUE", "FALSE", "T", "F", "YES", "NO", "SI", "Y", "N", "1", "0" 
            };

            // Patch: Implementation of "Stride Sampling" (Distributed Sampling).
            // Instead of reading the first 200 rows, we read 200 rows distributed evenly 
            // across the entire dataset to detect types in files with initial empty rows.
            int totalRows = rows.Count;
            int maxSamples = 200;
            int step = Math.Max(1, totalRows / maxSamples);

            for (int c = 0; c < headers.Count; c++) 
            {
                int vDate = 0, vTime = 0, vNum = 0, vBool = 0, validCount = 0;
                int maxLen = 1;
                int maxDec = 0;
                bool hasDecimalPoint = false;

                // Iterate using stride step
                for (int r = 0; r < totalRows; r += step) 
                {
                    if (c >= rows[r].Length) continue;
                    
                    string s = rows[r][c]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(s)) continue;
                    
                    validCount++; 
                    if (s.Length > maxLen) maxLen = s.Length;

                    // Priority 1: Time
                    if (regexTime.IsMatch(s)) 
                    {
                        vTime++;
                    }
                    // Priority 2: Date
                    else if ((s.Contains('/') || s.Contains('-')) && 
                            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtCheck) && 
                            dtCheck.Year >= 1900)
                    {
                        vDate++;
                    }
                    // Priority 3: Numeric
                    else if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal)) 
                    { 
                        vNum++; 
                        if (s.Contains('.') || s.Contains(',')) 
                        {
                            hasDecimalPoint = true;
                            int idxSep = s.LastIndexOfAny(new char[] { '.', ',' });
                            if (idxSep >= 0) {
                                int decs = s.Length - idxSep - 1;
                                if (decs > maxDec) maxDec = decs;
                            }
                        }
                    }
                    // Priority 4: Boolean
                    else 
                    {
                        if (boolSet.Contains(s)) vBool++;
                    }
                }

                string nL = CorrectName(headers[c]);
                var col = new DetectedColumn { OriginalName = nL };
                
                // Threshold: 50% of valid data must match type
                double threshold = validCount * 0.5;

                if (validCount == 0) 
                { 
                    col.SuggestedType = 0; // Character
                    col.Length = 1; 
                    col.IsGhostColumn = true; 
                }
                else 
                {
                    col.IsGhostColumn = false;
                    
                    if (vTime > threshold) 
                    { 
                        col.SuggestedType = 5; // Time
                        col.Length = Math.Max(8, maxLen); 
                    } 
                    else if (vDate > threshold) 
                    { 
                        col.SuggestedType = 3; // Date
                        col.Length = 8; 
                    }
                    else if (vNum > threshold) 
                    { 
                        col.SuggestedType = hasDecimalPoint ? 1 : 2; 
                        col.Length = Math.Max(10, maxLen); 
                        col.Decimals = maxDec; 
                    }
                    else if (vBool > threshold) 
                    { 
                        col.SuggestedType = 4; // Logical
                        col.Length = 1; 
                    }
                    else 
                    { 
                        col.SuggestedType = 0; // Character
                        col.Length = Math.Min(254, maxLen + 10); 
                    }
                }
                columns.Add(col);
            }
            return columns;
        }

        private string CorrectName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "EMPTY_FIELD";
            string clean = Regex.Replace(rawName, @"[^a-zA-Z0-9_]", "_");
            if (char.IsDigit(clean[0])) clean = "_" + clean;
            if (clean.Length > 128) clean = clean.Substring(0, 128);
            return clean.ToUpper();
        }

        // ==========================================================================================
        // UI SUPPORT METHODS
        // ==========================================================================================

        private void OnMergeToggled(bool active) {
            if (MergeFilenameInput != null) {
                MergeFilenameInput.Visible = active;
                if (active && string.IsNullOrEmpty(MergeFilenameInput.Text)) 
                    MergeFilenameInput.Text = "Master_Fusion"; 
            }

            foreach (Node n in SheetListVBox.GetChildren()) {
                if (n is SheetCard card) {
                    if (card.OutputFilenameInput != null) {
                        card.OutputFilenameInput.Editable = !active;
                        card.OutputFilenameInput.Modulate = !active ? Colors.White : new Color(1,1,1, 0.5f);
                    }
                }
            }
        }

        private void OnFolderSelected(string dir) {
            if (_isSelectingOutputFolder) {
                _globalOutputPath = dir;
                UpdatePathLabel();
                SavePreferences();
                LogUser($"Global folder changed to: {dir}");
            }
        }

        private void UpdatePathLabel() {
            if (PathLabel != null) 
                PathLabel.Text = string.IsNullOrEmpty(_globalOutputPath) ? "Same as source (Default)" : _globalOutputPath;
        }

        private void ToggleInspector(Control targetContent) {
            if (InspectorPanel == null || targetContent == null) return;
            bool isOpen = InspectorOverlay.Visible;
            bool isSameTarget = targetContent.Visible;

            // Hide everything
            if (SettingsView != null) SettingsView.Visible = false;
            if (LogsView != null) LogsView.Visible = false;
            if (AboutView != null) AboutView.Visible = false;

            if (isOpen && isSameTarget) {
                CerrarInspector();
                LogUser("Inspector closed by toggle.");
            } else {
                targetContent.Visible = true;
                if (!isOpen && _animPlayer != null) _animPlayer.Play("OpenInspector");
                LogUser($"Inspector opened: {targetContent.Name}");
            }
        }

        private void CerrarInspector() {
            if (InspectorOverlay != null && InspectorOverlay.Visible) {
                if (_animPlayer != null) _animPlayer.Play("CloseInspector");
                InspectorOverlay.Visible = false;
                
                _isViewingIndividualLogs = false;
            }
        }

        private void UpdateCounter() {
            int count = SheetListVBox.GetChildCount();
            if (FileQueueLabel != null) FileQueueLabel.Text = $"Files in queue: {count}";
            if (DropZonePanel != null) DropZonePanel.Visible = (count == 0);
        }

        private void OpenFileBrowser() {
            _isSelectingOutputFolder = false;
            OpenFileDialog.FileMode = FileDialog.FileModeEnum.OpenFiles;
            OpenFileDialog.Filters = new string[] { "*.xlsx, *.xls, *.ods, *.csv, *.dbf ; Data Sheets" };
            OpenFileDialog.PopupCentered();
        }

        private void AbrirSeleccionCarpeta() {
            _isSelectingOutputFolder = true;
            OpenFileDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
            OpenFileDialog.PopupCentered();
        }

        private void OpenSmartFolder() {
            string path = _globalOutputPath;
            if (string.IsNullOrEmpty(path)) {
                if (SheetListVBox.GetChildCount() > 0 && SheetListVBox.GetChild(0) is SheetCard card) {
                     path = Path.GetDirectoryName(card.AssociatedData.SourceFullPath);
                } else {
                     path = OS.GetSystemDir(OS.SystemDir.Documents);
                }
            }
            if (!string.IsNullOrEmpty(path)) OS.ShellOpen(path);
        }

        private void ClearAll() {
            foreach(Node n in SheetListVBox.GetChildren()) n.QueueFree();
            GetTree().CreateTimer(0.05).Timeout += UpdateCounter;
            LogUser("User cleared file list.");
        }

        private void ToggleLoading(bool v, string t="") { 
            if(LoadingToast != null) LoadingToast.Visible = v; 
            if(ProcessingLabel != null) ProcessingLabel.Text = t; 
            if(ProcessFilesBtn != null) ProcessFilesBtn.Disabled = v; 
        }

        public void ShowIndividualLogs(string logContent) {
            if (LogsView == null || LogRichText == null) return;
            
            _isViewingIndividualLogs = true; // Activate lock
            LogRichText.Text = logContent;
            
            // Open inspector manually to avoid circular logging
            if (InspectorPanel != null && !InspectorOverlay.Visible) {
                LogsView.Visible = true;
                SettingsView.Visible = false; 
                AboutView.Visible = false;
                InspectorOverlay.Visible = true;
                if (_animPlayer != null) _animPlayer.Play("OpenInspector");
            } else {
                // If already open, just switch views
                LogsView.Visible = true;
                SettingsView.Visible = false;
                AboutView.Visible = false;
            }
        }

        // --- EVENT PROXIES ---
        private void OnFileSelected(string path) => OnFilesDropped(new string[] { path });
        private void OnFilesSelected(string[] paths) => OnFilesDropped(paths);

        // --- PREFERENCE PERSISTENCE ---
        
        private void SavePreferences() {
            _currentConfig.LastOutputPath = _globalOutputPath;
            if (DarkModeSwitch != null) _currentConfig.IsDarkMode = DarkModeSwitch.ButtonPressed;

            var jsonString = System.Text.Json.JsonSerializer.Serialize(_currentConfig);
            
            using var file = Godot.FileAccess.Open(_configPath, Godot.FileAccess.ModeFlags.Write);
            if (file != null) {
                file.StoreString(jsonString);
            }
        }

        private void LoadPreferences() {
            if (!Godot.FileAccess.FileExists(_configPath)) return;

            using var file = Godot.FileAccess.Open(_configPath, Godot.FileAccess.ModeFlags.Read);
            if (file != null) {
                string jsonString = file.GetAsText();
                try {
                    var data = System.Text.Json.JsonSerializer.Deserialize<UserConfig>(jsonString);
                    if (data != null) _currentConfig = data;
                } catch { 
                    LogErr("Corrupt configuration, using defaults.");
                }
            }
        }
    
        // ==========================================================================================
        // DOCUMENTATION SYSTEM (OFFLINE SUPPORT)
        // ==========================================================================================

        /// <summary>
        /// Manages the opening of the documentation. Verifies if files exist locally in AppData.
        /// If not (first run or update), extracts them from the res:// package.
        /// Finally, opens index.html in the system's default browser.
        /// </summary>
        private async void OpenLocalDocumentation()
        {
            try 
            {
                // Source path within the exported PCK/Executable
                string sourceResPath = "res://db-forge-docs/_build/html";
                // Target path in user's local data folder (AppData on Windows)
                string targetUserDir = "user://docs/html";
                string targetIndexFile = Path.Combine(targetUserDir, "index.html");

                // Check if extraction is needed (folder missing or index missing)
                // Note: For versioning updates, a version check file could be implemented here.
                using var dirAccess = DirAccess.Open(targetUserDir);
                bool needsExtraction = (dirAccess == null) || !Godot.FileAccess.FileExists(targetIndexFile);

                if (needsExtraction)
                {
                    LogSys("[DOCS] Local documentation missing. Starting extraction from package...");
                    ToggleLoading(true, "Extracting Help Files...");
                    
                    // Allow UI to refresh before blocking thread with IO operations
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                    bool success = ExtractDocumentationRecursive(sourceResPath, targetUserDir);
                    
                    ToggleLoading(false);
                    
                    if (!success)
                    {
                        LogErr("[DOCS] Failed to extract documentation files.");
                        ShowIndividualLogs("[COLOR=red]Error: Could not extract documentation files from application package.[/COLOR]");
                        return;
                    }
                    LogSys("[DOCS] Extraction complete.");
                }

                // Get absolute OS path so the browser can find it
                string globalPath = ProjectSettings.GlobalizePath(targetIndexFile);
                
                LogUser($"Opening documentation: {globalPath}");
                OS.ShellOpen(globalPath);
            }
            catch (Exception ex)
            {
                LogErr($"[DOCS] Critical error opening help: {ex.Message}");
                ToggleLoading(false);
            }
        }

        /// <summary>
        /// Recursively traverses the Godot virtual directory (res://) and copies its content
        /// to the user file system (user://). Essential for exported builds.
        /// </summary>
        private bool ExtractDocumentationRecursive(string sourceDir, string destDir)
        {
            var dir = DirAccess.Open(sourceDir);
            if (dir == null) 
            {
                LogErr($"[DOCS] Source directory not found: {sourceDir}");
                return false;
            }

            // Create destination directory if it doesn't exist
            if (!DirAccess.DirExistsAbsolute(destDir))
            {
                var err = DirAccess.MakeDirRecursiveAbsolute(destDir);
                if (err != Error.Ok) return false;
            }

            dir.ListDirBegin();
            string fileName = dir.GetNext();

            while (fileName != "")
            {
                if (dir.CurrentIsDir())
                {
                    // Ignore navigation directories (. and ..)
                    if (fileName != "." && fileName != "..")
                    {
                        string subSource = sourceDir + "/" + fileName;
                        string subDest = destDir + "/" + fileName;
                        if (!ExtractDocumentationRecursive(subSource, subDest)) return false;
                    }
                }
                else
                {
                    // It is a file, perform copy
                    // Use Godot.FileAccess to read from package and write to disk
                    string srcPath = sourceDir + "/" + fileName;
                    string dstPath = destDir + "/" + fileName;

                    // Import: .import files should not be copied, they are editor metadata
                    if (!fileName.EndsWith(".import"))
                    {
                        using var srcFile = Godot.FileAccess.Open(srcPath, Godot.FileAccess.ModeFlags.Read);
                        if (srcFile != null)
                        {
                            var buffer = srcFile.GetBuffer((long)srcFile.GetLength());
                            using var dstFile = Godot.FileAccess.Open(dstPath, Godot.FileAccess.ModeFlags.Write);
                            dstFile.StoreBuffer(buffer);
                        }
                    }
                }
                fileName = dir.GetNext();
            }
            return true;
        }
    }

    public static class StringExtensions { 
        public static string Left(this string s, int l) => string.IsNullOrEmpty(s)?s:(s.Length<=l?s:s.Substring(0,l)); 
    }
}