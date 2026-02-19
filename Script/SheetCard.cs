/*
    DBF Forge - Data Forging Tool.
    Copyright (C) 2026 YirehStudios
    
    Description: 
    Controls the visual "Card" for each detected spreadsheet.
    Allows the user to configure data types, lengths, and view errors in real-time.
*/

using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;
using DbfDataReader;
using DbfForge.UI; // Required to reference MainMenu and its inner types

namespace DbfForge.UI.Components
{
    /// <summary>
    /// UI Component representing an individual sheet from the imported file.
    /// Contains logic for modifying the output structure (Types, Lengths, Decimals).
    /// </summary>
    public partial class SheetCard : PanelContainer
    {
        // UI Elements mapped from Godot Editor
        [Export] public CheckBox EnableCheckbox;
        [Export] public Label SourceFileLabel;
        [Export] public LineEdit OutputFilenameInput;
        [Export] public OptionButton FormatDropdown; 
        [Export] public Label StatusLabel; 
        
        [Export] public Button ToggleFieldsBtn;
        [Export] public Control FieldsEditorContainer;
        [Export] public VBoxContainer FieldsListVBox; 
        [Export] public Button AddManualFieldBtn;

        [Export] public Button TogglePreviewBtn;
        [Export] public Control DataPreviewContainer;
        [Export] public GridContainer PreviewGrid;
        [Export] public Button OutputFolderBtn;
        [Export] public Button LogsBtn;
        [Export] public Button EditBtn;
        [Export] public Control LoadingOverlay;
        [Export] public ProgressBar CardProgressBar;

        // Internal references
        private AnimationPlayer _animPlayer;
        private string _individualOutputPath = "";
        private readonly List<string> _internalLog = new List<string>();
        
        // Public reference to data objects (Updated to use MainMenu.SheetData)
        public MainMenu.SheetData AssociatedData;
        private MainMenu _mainMenu;

        private VisualCardHelper _visualHelper;
        public event Action OnManualFormatChange;

        public override void _Ready()
        {
            // Initialize internal references
            _visualHelper = GetNodeOrNull<VisualCardHelper>("VisualHelper");
            
            // Safe assignment of AnimationPlayer
            var animNode = GetNodeOrNull("AnimPlayer"); 
            if (animNode is AnimationPlayer ap) _animPlayer = ap;

            // Patch: Initialize Debounce Timer dynamically to fix UI lag on typing.
            // This avoids adding a private field to the class structure but adds the functionality.
            var debounceTimer = new Timer { Name = "DebounceTimer", WaitTime = 0.5f, OneShot = true };
            debounceTimer.Timeout += ExecuteDeferredLiveDebug; // Hook to actual logic
            AddChild(debounceTimer);

            // 1. Initial State (Hidden Panels)
            if (FieldsEditorContainer != null) FieldsEditorContainer.Visible = false;
            if (DataPreviewContainer != null) DataPreviewContainer.Visible = false;
            if (TogglePreviewBtn != null) TogglePreviewBtn.Visible = false;
            if (LoadingOverlay != null) LoadingOverlay.Visible = false;
            
            // Extra buttons hidden at start
            if (LogsBtn != null) LogsBtn.Visible = false; 
            if (EditBtn != null) EditBtn.Visible = false;

            // 2. Event: Configure Button (Expand/Collapse)
            if (ToggleFieldsBtn != null) 
            {
                ToggleFieldsBtn.Pressed += () => { 
                    if (FieldsEditorContainer.Visible) {
                        _animPlayer?.Play("Collapse");
                        ToggleFieldsBtn.Text = "Configure Fields";
                    } else {
                        _animPlayer?.Play("Expand");
                        ToggleFieldsBtn.Text = "Hide Configuration";
                    }
                };
            }

            // 3. Event: View Data Button
            if (TogglePreviewBtn != null) 
            {
                TogglePreviewBtn.Pressed += () => {
                    DataPreviewContainer.Visible = !DataPreviewContainer.Visible;
                    TogglePreviewBtn.Text = DataPreviewContainer.Visible ? "Hide Data" : "View Data";
                };
            }

            // 4. Event: Action Buttons
            if (AddManualFieldBtn != null) AddManualFieldBtn.Pressed += OnManualAddField;
            if (OutputFolderBtn != null) OutputFolderBtn.Pressed += SelectIndividualPath;
            
            // 5. Event: Logs Button (Calls main menu viewer)
            if (LogsBtn != null) {
                LogsBtn.Pressed += () => {
                    _mainMenu?.ShowIndividualLogs(string.Join("\n", _internalLog));
                };
            }

            // 6. Event: Format Change (Synchronization with Menu)
            // Notify parent only when change comes from user interaction
            if (FormatDropdown != null) {
                FormatDropdown.ItemSelected += (idx) => {
                    OnManualFormatChange?.Invoke();
                };
            }
        }

        /// <summary>
        /// Configures the card with data analyzed by the Menu.
        /// </summary>
        public void ConfigureSheet(MainMenu.SheetData data, MainMenu menu)
        {
            AssociatedData = data;
            _mainMenu = menu;
            
            // Display original file name
            if (SourceFileLabel != null)
                SourceFileLabel.Text = System.IO.Path.GetFileName(data.SourceFileName).ToUpper();
                
            SetStatus(0); // State 0 = Queued

            // Generate output name suggestion
            string baseName = System.IO.Path.GetFileNameWithoutExtension(data.SourceFileName);
            string cleanSheetName = Regex.Replace(data.SheetName, @"[^a-zA-Z0-9_]", "");
            
            if (cleanSheetName.Length > 0 && cleanSheetName.ToLower() != "hoja1" && cleanSheetName.ToLower() != "sheet1") 
                baseName += "_" + cleanSheetName;
            
            if (OutputFilenameInput != null)
                OutputFilenameInput.Text = baseName;
        }

        /// <summary>
        /// Triggers the entry animation via the VisualHelper.
        /// </summary>
        public void AnimateEntry(float delay)
        {
            var visual = GetNodeOrNull<VisualCardHelper>("VisualHelper");
            visual?.AnimateEntry(delay);
        }

        /// <summary>
        /// Iterates through the detected Excel structure and calls the creator
        /// to draw each row UI.
        /// </summary>
        public void GenerateUIColumns()
        {
            // Null check for critical dependencies
            if (FieldsListVBox == null)
            {
                GD.PrintErr("SheetCard: FieldsListVBox is null. Cannot generate UI.");
                return;
            }

            // Correction: Ensure AssociatedData and Structure are valid to prevent NullReferenceException
            if (AssociatedData == null || AssociatedData.Structure == null)
            {
                GD.Print("SheetCard: No structure data available yet to generate columns.");
                return;
            }

            foreach(Node n in FieldsListVBox.GetChildren()) {
                n.QueueFree();
            }

            int realIndex = 0;
            // Iterate using the English property 'Structure' from SheetData
            foreach(var col in AssociatedData.Structure) {
                
                // CRITICAL CORRECTION: Filter Visual Garbage
                // If it is a ghost column, ignore it completely.
                if (col.IsGhostColumn) {
                    realIndex++;
                    continue; 
                }
                
                CreateVisualRow(col.OriginalName, col.SuggestedType, col.Length, col.Decimals, realIndex, false);
                
                realIndex++;
            }
        }

        /// <summary>
        /// Event fired by "Add Field". 
        /// Instantiates a new visual row for a manual column (not existing in source file).
        /// </summary>
        private void OnManualAddField()
        {
            // Create field: Name="NEW", Type=Character(0), Length=50, Dec=0, Index=-1 (Manual)
            CreateVisualRow("NEW", 0, 50, 0, -1, false);
            
            // Open config area so user sees what they added
            if (FieldsEditorContainer != null) FieldsEditorContainer.Visible = true;
        }

        /// <summary>
        /// Scans all active visual rows and extracts user-defined default values.
        /// Vital for filling manual columns with fixed data (e.g., 'USD', 'ACTIVE').
        /// </summary>
        /// <returns>List of strings with fixed values (or null if it's an Excel column).</returns>
        public List<string> GetDefaultValues() {
            var values = new List<string>();
            foreach(Node child in FieldsListVBox.GetChildren()) {
                if (child is not HBoxContainer hbox) continue;
                
                var check = hbox.GetNode<CheckBox>("BoxName/ChkExportar");
                if (check.ButtonPressed) {
                    // Logic for reading default value input would go here
                    values.Add(null); 
                }
            }
            return values;
        }

        private void CreateVisualRow(string name, int type, int length, int decimals, int originIndex, bool isGhost)
        {
            var row = new HBoxContainer();
            row.SetMeta("idx_origin", originIndex);
            row.AddThemeConstantOverride("separation", 12);

            // --- 1. Name and Checkbox ---
            var boxName = new HBoxContainer { Name = "BoxName", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2.0f };
            boxName.AddThemeConstantOverride("separation", 10);

            var chk = new CheckBox { Name = "ChkExportar", Text = "", MouseDefaultCursorShape = Control.CursorShape.PointingHand, ButtonPressed = !isGhost };
            chk.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f)); 

            var editName = new LineEdit { Name = "EditNombre", Text = SanitizeName(name), MaxLength = 128, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            editName.AddThemeStyleboxOverride("normal", GetFlatStyle(new Color(0.05f, 0.05f, 0.06f)));
            editName.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));

            boxName.AddChild(chk);
            boxName.AddChild(editName);

            // --- 2. Type Selector ---
            var optType = new OptionButton { Name = "OptTipo", CustomMinimumSize = new Vector2(120, 0) };
            optType.AddItem("Character", 0); optType.AddItem("Numeric", 1); 
            optType.AddItem("Integer", 2); optType.AddItem("Date", 3); 
            optType.AddItem("Logical", 4); optType.AddItem("Time", 5);
            optType.Selected = type;
            optType.AddThemeStyleboxOverride("normal", GetFlatStyle(new Color(0.12f, 0.12f, 0.14f)));
            optType.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));

            // --- 3. Length and Decimals ---
            var spinLen = new SpinBox { Name = "SpinLargo", MinValue=1, MaxValue=254, Value=length, CustomMinimumSize = new Vector2(70, 0), Alignment = HorizontalAlignment.Center };
            spinLen.GetLineEdit().AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));

            var spinDec = new SpinBox { Name = "SpinDec", MinValue=0, MaxValue=18, Value=decimals, CustomMinimumSize = new Vector2(60, 0), Alignment = HorizontalAlignment.Center };
            spinDec.GetLineEdit().AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));

            // --- 4. State Indicator ---
            var lblState = new Label { Name = "LblState", Text = "✔", CustomMinimumSize = new Vector2(30, 0), HorizontalAlignment = HorizontalAlignment.Center };
            lblState.AddThemeColorOverride("font_color", new Color("10B981"));

            // --- FORENSIC LOGIC (DETAILED LOGS) ---
            
            // 1. Log on Enable/Disable
            chk.Toggled += (active) => {
                editName.Editable = active;
                optType.Disabled = !active;
                editName.Modulate = active ? Colors.White : new Color(1,1,1, 0.5f);
                
                LogAction($"Field '{editName.Text}' was {(active ? "enabled" : "disabled")}.");
                
                if (!active) { 
                    lblState.Text = "-"; 
                    lblState.AddThemeColorOverride("font_color", Colors.Gray);
                } else {
                    optType.EmitSignal(OptionButton.SignalName.ItemSelected, optType.Selected);
                }
                LiveDebug();
            };

            // 2. Log on Type Change
            optType.ItemSelected += (idx) => {
                string newType = optType.GetItemText((int)idx);
                LogAction($"Field '{editName.Text}' changed type to: {newType}");
                
                // Business Rules
                int t = (int)idx;
                bool useDec = (t == 1);
                spinDec.Editable = useDec;
                spinDec.Modulate = useDec ? Colors.White : new Color(1,1,1,0.3f);
                if(!useDec) spinDec.Value = 0;

                bool isFixed = (t==2 || t==3 || t==4);
                spinLen.Editable = !isFixed;
                spinLen.Modulate = !isFixed ? Colors.White : new Color(1,1,1,0.5f);

                if(t==0) { spinLen.MaxValue=254; spinLen.Value=Math.Max(1, spinLen.Value); }
                if(t==1) { spinLen.MaxValue=20; spinDec.MaxValue=Math.Max(0, spinLen.Value-2); }
                if(t==2) spinLen.Value=4; 
                if(t==3) spinLen.Value=8;
                if(t==4) spinLen.Value=1;
                if(t==5) { spinLen.MaxValue=50; spinLen.Value=Math.Max(8, spinLen.Value); }
                
                LiveDebug();
            };

            // 3. Log on Rename (only on focus exit to avoid spam)
            editName.FocusExited += () => {
                if (editName.Text != name) // If changed from original
                    LogAction($"Field renamed from '{name}' to '{editName.Text}'");
            };

            // Direct inputs for debugging
            spinLen.ValueChanged += (val) => LiveDebug();
            spinDec.ValueChanged += (val) => LiveDebug();
            editName.TextChanged += (txt) => LiveDebug();

            // Initialize visual state without triggering logs
            if (!isGhost) {
                // Simulate initial logic
                int t = type;
                bool useDec = (t == 1);
                spinDec.Editable = useDec;
                spinDec.Modulate = useDec ? Colors.White : new Color(1,1,1,0.3f);
                bool isFixed = (t==2 || t==3 || t==4);
                spinLen.Editable = !isFixed;
                spinLen.Modulate = !isFixed ? Colors.White : new Color(1,1,1,0.5f);
            }

            row.AddChild(boxName);
            row.AddChild(optType);
            row.AddChild(spinLen);
            row.AddChild(spinDec);
            row.AddChild(lblState);
            
            FieldsListVBox.AddChild(row);
        }

        // Helper for style boxes
        private StyleBoxFlat GetFlatStyle(Color bg) {
            var sb = new StyleBoxFlat();
            sb.BgColor = bg;
            sb.CornerRadiusTopLeft = 4; sb.CornerRadiusTopRight = 4;
            sb.CornerRadiusBottomLeft = 4; sb.CornerRadiusBottomRight = 4;
            sb.ContentMarginLeft = 8; sb.ContentMarginRight = 8;
            return sb;
        }

        /// <summary>
        /// Reads the generated file (DBF, CSV, or Excel) and shows a quick preview in the UI.
        /// Supports multiple formats to verify export correctness.
        /// </summary>
        /// <param name="filePath">Full path of the generated file.</param>
        public void PopulatePreviewResults(string filePath)
        {
            // Clear previous results to avoid duplication
            foreach(Node n in PreviewGrid.GetChildren()) n.QueueFree();
            
            try {
                string ext = System.IO.Path.GetExtension(filePath).ToLower();

                // === CASE 1: DBF PREVIEW ===
                if (ext == ".dbf") {
                    // Use DbfDataReader with Windows-1252 encoding (DBF standard)
                    var options = new DbfDataReaderOptions { Encoding = Encoding.GetEncoding(1252) };
                    using (var dbfReader = new DbfDataReader.DbfDataReader(filePath, options)) {
                        PreviewGrid.Columns = dbfReader.FieldCount;
                        
                        // Generate Headers
                        for(int i=0; i<dbfReader.FieldCount; i++) 
                            CreateHeaderCell(dbfReader.GetName(i));
                        
                        // Show first 20 records
                        int c = 0;
                        while(dbfReader.Read() && c++ < 20) {
                            for(int i=0; i<dbfReader.FieldCount; i++) 
                                CreateDataCell(dbfReader.GetValue(i)?.ToString() ?? "");
                        }
                    }
                }
                // === CASE 2: CSV OR TXT PREVIEW ===
                else if (ext == ".csv" || ext == ".txt") {
                    // Optimized: Use Regex to handle commas inside quotes correctly (Standard CSV RFC 4180)
                    var csvRegex = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

                    using (var sr = new System.IO.StreamReader(filePath, Encoding.UTF8)) {
                        string line = sr.ReadLine();
                        if (line != null) {
                            // Header parsing with regex
                            var headers = csvRegex.Split(line); 
                            PreviewGrid.Columns = headers.Length;
                            foreach(var h in headers) CreateHeaderCell(h.Replace("\"", "").Trim());
                            
                            // Data parsing (First 20 rows)
                            int c = 0;
                            while((line = sr.ReadLine()) != null && c++ < 20) {
                                var parts = csvRegex.Split(line); 
                                foreach(var p in parts) CreateDataCell(p.Replace("\"", "").Trim());
                            }
                        }
                    }
                }
                // === CASE 3: EXCEL PREVIEW (.xlsx) ===
                else if (ext == ".xlsx") {
                    // Using System.IO explicitly to avoid conflict with Godot.FileAccess
                    using (var fs = System.IO.File.Open(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    using (var reader = new DbfForge.Core.Readers.ExcelReaderNPOI(fs, ext)) {
                        // Try reading first available sheet
                        if (reader.MoveToNextSheet()) { 
                            // Read Headers (First Row)
                            if (reader.ReadRow()) {
                                PreviewGrid.Columns = reader.FieldCount;
                                for(int i=0; i<reader.FieldCount; i++) 
                                    CreateHeaderCell(reader.GetValue(i)?.ToString() ?? "");
                            }
                            // Read Data (First 20 rows)
                            int c = 0;
                            while(reader.ReadRow() && c++ < 20) {
                                for(int i=0; i<reader.FieldCount; i++) 
                                    CreateDataCell(reader.GetValue(i)?.ToString() ?? "");
                            }
                        }
                    }
                }

            } catch (Exception e) { 
                GD.PrintErr("Error previewing results: " + e.Message); 
                var l = new Label { Text = "Error opening preview: " + e.Message };
                l.AddThemeColorOverride("font_color", Colors.Red);
                PreviewGrid.AddChild(l);
            }
        }

        private void CreateHeaderCell(string text) {
            var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
            l.AddThemeColorOverride("font_color", new Color(0.13f, 0.74f, 0.14f)); 
            PreviewGrid.AddChild(l);
        }

        private void CreateDataCell(string text) {
            var l = new Label { Text = text, ClipText = true, CustomMinimumSize = new Vector2(50, 0) };
            l.AddThemeColorOverride("font_color", Colors.White);
            l.TooltipText = text; // Tooltip to see full text
            PreviewGrid.AddChild(l);
        }

        /// <summary>
        /// Public entry point for validation.
        /// Patch: Implements Debounce pattern. Resets timer on every call.
        /// Actual logic execution happens when timer triggers ExecuteDeferredLiveDebug.
        /// </summary>
       public void LiveDebug()
        {
            var timer = GetNodeOrNull<Timer>("DebounceTimer");
            if (timer != null)
            {
                timer.Stop();
                timer.Start(); // Reset countdown
            }
            else
            {
                // Fallback if timer missing
                ExecuteDeferredLiveDebug();
            }
        }

        /// <summary>
        /// Internal method containing the heavy O(N*M) validation logic.
        /// Called only after user stops typing for 500ms.
        /// </summary>
        /// <summary>
/// Internal method containing the heavy O(N*M) validation logic.
/// Called only after user stops typing for 500ms.
/// </summary>
        private void ExecuteDeferredLiveDebug()
        {
            // Verificación de ciclo de vida: Si el nodo ya no está en el árbol, abortar inmediatamente
            // para evitar acceso a objetos liberados o condiciones de carrera.
            if (!IsInsideTree() || AssociatedData == null || AssociatedData.DebugSample == null) return;

            int colIndex = 0;
            int visualErrorsCount = 0; // Contador de semáforos rojos

            foreach(Node child in FieldsListVBox.GetChildren()) {
                // Validación de tipo segura antes de casting
                if (child is not HBoxContainer hbox) continue;

                var chk = hbox.GetNode<CheckBox>("BoxName/ChkExportar");
                // Si la columna está deshabilitada, saltamos la validación visual pero incrementamos el índice lógico
                if (!chk.ButtonPressed) { colIndex++; continue; }

                var opt = hbox.GetNode<OptionButton>("OptTipo");
                var lblState = hbox.GetNode<Label>("LblState");
                var edit = hbox.GetNode<LineEdit>("BoxName/EditNombre");
                var spinLength = hbox.GetNode<SpinBox>("SpinLargo");

                lblState.TooltipText = ""; 
                int rowErrors = 0;
                string msg = "";

                // 1. Name Validation (Integridad estructural)
                if (string.IsNullOrWhiteSpace(edit.Text)) {
                    rowErrors++;
                    msg = "Field name cannot be empty.";
                }

                // 2. Validation against Sample Data (Integridad de datos - Truncamiento)
                foreach(var row in AssociatedData.DebugSample) {
                    // Protección contra desbordamiento de índice si la fila es más corta que la estructura
                    if (colIndex < row.Length) {
                        var data = row[colIndex];
                        string strVal = data?.ToString() ?? "";
                        
                        // Error: El texto excede la longitud definida para tipos Carácter o Numérico
                        // Esto alerta al usuario sobre posible pérdida de datos antes de exportar
                        if ((opt.Selected == 0 || opt.Selected == 1) && strVal.Length > spinLength.Value) {
                                rowErrors++;
                                msg = $"Sample data '{strVal}' exceeds length ({spinLength.Value}). Potential data loss.";
                                break; // Un solo error es suficiente para marcar la fila
                        }
                    }
                }

                // Actualización Visual de la Fila (Feedback de Usuario)
                if (rowErrors > 0) { 
                    edit.AddThemeColorOverride("font_color", new Color("EF4444")); // Rojo
                    lblState.Text = "!";
                    lblState.AddThemeColorOverride("font_color", new Color("EF4444"));
                    lblState.TooltipText = msg;
                    visualErrorsCount++;
                } else { 
                    edit.AddThemeColorOverride("font_color", new Color("E4E4E7")); // Blanco
                    lblState.Text = "✔";
                    lblState.AddThemeColorOverride("font_color", new Color("10B981")); // Verde
                }
                colIndex++;
            }

            // --- STATE UPDATE ---
            // Si se detectan errores visuales en la configuración, cambiamos el estado global de la tarjeta
            if (visualErrorsCount > 0) {
                SetStatus(5); // "RISK" (Naranja) - Indica configuración peligrosa
            } 
            else {
                // Si no hay riesgos detectados, estado listo para procesar
                SetStatus(0); // "READY" (Cian)
            }
        }

        /// <summary>
        /// Compiles the current UI configuration to generate the "Ticket".
        /// </summary>
        public List<MainMenu.DetectedColumn> GetUIDefinitions() {
            var definitions = new List<MainMenu.DetectedColumn>();
            var usedNames = new HashSet<string>(); 

            foreach(Node child in FieldsListVBox.GetChildren()) {
                if (child is not HBoxContainer hbox) continue;

                var chk = hbox.GetNode<CheckBox>("BoxName/ChkExportar");
                if (!chk.ButtonPressed) continue;

                string baseName = hbox.GetNode<LineEdit>("BoxName/EditNombre").Text.Trim().ToUpper();
                if (string.IsNullOrEmpty(baseName)) baseName = "FIELD";
                baseName = SanitizeName(baseName);

                // Anti-Duplicate Logic
                string finalName = baseName;
                int counter = 1;
                while (usedNames.Contains(finalName)) {
                    string root = baseName.Length > 8 ? baseName.Substring(0, 8) : baseName;
                    finalName = $"{root}_{counter}";
                    counter++;
                }
                usedNames.Add(finalName);

                // Using English properties for DetectedColumn
                definitions.Add(new MainMenu.DetectedColumn {
                    OriginalName = finalName,
                    SuggestedType = hbox.GetNode<OptionButton>("OptTipo").Selected,
                    Length = (int)hbox.GetNode<SpinBox>("SpinLargo").Value,
                    Decimals = (int)hbox.GetNode<SpinBox>("SpinDec").Value
                });
            }
            return definitions;
        }

        /// <summary>
        /// Generates an index map to relate filtered columns with raw data.
        /// </summary>
        /// <returns>List of original indices to be read.</returns>
        public List<int> GetActiveIndices() {
            var indices = new List<int>();
            foreach(Node child in FieldsListVBox.GetChildren()) {
                if (child is not HBoxContainer hbox) continue;
                
                var check = hbox.GetNode<CheckBox>("BoxName/ChkExportar");
                if (check.ButtonPressed) {
                    if (hbox.HasMeta("idx_origin"))
                        indices.Add((int)hbox.GetMeta("idx_origin"));
                    else
                        indices.Add(-1);
                }
            }
            return indices;
        }

        private string SanitizeName(string n) => StringExtensions.Left(Regex.Replace(n.ToUpper(), @"[^A-Z0-9_]", "_"), 128);

        private void SelectIndividualPath() {
            var dialog = new FileDialog();
            dialog.FileMode = FileDialog.FileModeEnum.OpenDir;
            dialog.Access = FileDialog.AccessEnum.Filesystem;
            dialog.UseNativeDialog = true;
            dialog.Title = "Select output folder for this file";
            AddChild(dialog);
            
            dialog.DirSelected += (dir) => {
                _individualOutputPath = dir;
                // Visual feedback: Paint icon green
                if(OutputFolderBtn != null) OutputFolderBtn.Modulate = new Color("10B981"); 
                dialog.QueueFree();
            };
            dialog.Show();
        }
        
        // This method is used by MainMenu.cs to determine where to save
        public string GetEffectiveOutputPath(string globalPath) {
            return string.IsNullOrEmpty(_individualOutputPath) ? globalPath : _individualOutputPath;
        }

        public void AddLog(string message, bool isWarning = false) 
        {
            // 1. Internal Logging (For individual UI viewing)
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string color = isWarning ? "yellow" : "gray";
            string type = isWarning ? "WARN" : "INFO";
            
            // RichTextLabel formatted entry
            _internalLog.Add($"[color={color}][{timestamp}] {type}: {message}[/color]");

            // 2. Propagation to Master Log (Disk Persistence and Global Log)
            if (_mainMenu != null)
            {
                // Use the filename as a context identifier
                string cardContext = SourceFileLabel?.Text ?? "UnknownCard";
                string formattedMsg = $"[{cardContext}] {message}";

                // Routing based on severity for correct global console coloring
                if (isWarning) 
                    _mainMenu.LogWarn(formattedMsg);
                else 
                    _mainMenu.LogSys(formattedMsg);
            }
        }

        public void SetLoading(bool active) {
            if (LoadingOverlay != null) {
                LoadingOverlay.Visible = active;
                if(active && CardProgressBar != null) CardProgressBar.Value = 0; 
            }
        }

        public void SetProgress(float value) {
            if (CardProgressBar != null) CardProgressBar.Value = value;
        }

        public void SetStatus(int state)
        {
            // 1. Delegate visual change to helper
            if (_visualHelper != null) {
                _visualHelper.UpdateVisualState(state);
            } else if (StatusLabel != null) {
                // Fallback
                StatusLabel.Text = (state == 2) ? "OK" : "...";
            }
            
            // 2. Functional Logic: Show buttons on completion
            // Final states: 2 (Success), 3 (Error), 4 (Warning)
            bool isFinished = (state >= 2); 
            
            if (isFinished) {
                if (LogsBtn != null) LogsBtn.Visible = true;
                if (EditBtn != null) EditBtn.Visible = true;
                if (TogglePreviewBtn != null) TogglePreviewBtn.Visible = true;
            }
            
            // Hide overlay if finished
            if (isFinished) SetLoading(false);
        }

        private void LogAction(string message) {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _internalLog.Add($"[color=cyan][{timestamp}] [USER] {message}[/color]");
        }
    }
}