using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using RevitLocationExporter.Configuration;
using RevitLocationExporter.Core;
using MicrosoftOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MicrosoftSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace RevitLocationExporter.Form
{
    /// <summary>
    /// Modern Fluent desktop Trade Profile Manager supporting full per-trade configuration,
    /// two-way Excel workbook synchronization, dependent Excel-like parameter formulas,
    /// live formula evaluation preview, categories/worksets, and project/sheet defaults.
    /// </summary>
    public sealed class TradeProfileManagerDialog : Window
    {
        private readonly Dictionary<string, TradeProfile> _workingProfiles =
            new(StringComparer.OrdinalIgnoreCase);

        private string? _currentTradeCode;
        private string _activeTradeCode;
        private bool _isUpdatingEditor;

        // Sidebar & Header
        private readonly WpfListBox _lbProfiles;
        private readonly Border _headerActivePill;
        private readonly TextBlock _txtFooterStatus;

        // Tab 1: Overview & Naming
        private readonly WpfTextBox _tbTradeCode;
        private readonly WpfTextBox _tbTradeName;
        private readonly WpfTextBox _tbDisciplineFolder;
        private readonly WpfTextBox _tbWorkPackageSubfolder;
        private readonly WpfTextBox _tbProjectNumber;
        private readonly WpfTextBox _tbOriginator;
        private readonly WpfTextBox _tbRvtTemplate;
        private readonly WpfTextBox _tbIfcTemplate;
        private readonly WpfTextBox _tbNwcTemplate;
        private readonly TextBlock _txtTemplatePreview;
        private readonly WpfTextBox _tbIfcJsonPath;
        private readonly WpfTextBox _tbModelCheckersFolder;

        // Tab 2: Excel Sync
        private readonly WpfTextBox _tbLinkedExcelPath;
        private readonly WpfCheckBox _cbAutoSyncExcel;
        private readonly Border _syncStatusPill;
        private readonly TextBlock _txtSyncTimestamp;
        private readonly TextBlock _txtMetricElem;
        private readonly TextBlock _txtMetricSheet;
        private readonly TextBlock _txtMetricSpecific;
        private readonly TextBlock _txtMetricStandards;
        private readonly TextBlock _txtMetricExports;
        private readonly WrapPanel _excelColumnsPanel;

        // Tab 3: Parameters & Formulas
        private readonly WpfDataGrid _dgRules;
        private readonly ObservableCollection<ParameterRuleRowVm> _ruleRows = new();
        private readonly WpfComboBox _cbSampleLocator;
        private readonly WpfTextBox _tbSampleInputs;
        private readonly TextBlock _txtFormulaStatus;

        // Tab 3B: Sheet Text Notes
        private readonly WpfDataGrid _dgTextNoteRules;
        private readonly ObservableCollection<TextNoteRuleRowVm> _textNoteRows = new();
        private readonly WpfTextBox _tbTnSampleSheet;
        private readonly WpfTextBox _tbTnSampleLocator;
        private readonly WpfTextBox _tbTnSampleInput;
        private readonly WpfTextBox _tbTnSampleOutput;
        private readonly TextBlock _txtTnStatus;

        // Tab 4: Categories & Worksets
        private readonly WpfTextBox _tbMatchTokens;
        private readonly WpfTextBox _tbWorksetSuffixes;
        private readonly WpfTextBox _tbCategorySearch;
        private readonly StackPanel _categoriesListStack;
        private readonly WpfTextBox _tbCustomCategory;
        private readonly TextBlock _txtCategoryCount;
        private readonly HashSet<string> _selectedCategories = new(StringComparer.OrdinalIgnoreCase);

        // Tab 5: Project, Sheets & Defaults
        private readonly WpfTextBox _tbSurveyN;
        private readonly WpfTextBox _tbSurveyE;
        private readonly WpfTextBox _tbSurveyZ;
        private readonly WpfTextBox _tbSheetIncludeToken;
        private readonly WpfTextBox _tbSheetDeletePatterns;
        private readonly WpfTextBox _tbSheetCoverName;
        private readonly WpfCheckBox _cbSyncWpCheckboxes;
        private readonly WpfDataGrid _dgProjectInfo;
        private readonly ObservableCollection<ProjectInfoEntryVm> _projectInfoRows = new();
        private readonly WpfCheckBox _cbDefFillParams;
        private readonly WpfCheckBox _cbDefCoordWorksets;
        private readonly WpfCheckBox _cbDefPurge;
        private readonly WpfCheckBox _cbDefNwc;
        private readonly WpfCheckBox _cbDefIfc;
        private readonly WpfCheckBox _cbDefRvt;
        private readonly WpfCheckBox _cbDefPdf;
        private readonly WpfCheckBox _cbDefDwg;
        private readonly WpfCheckBox _cbDefStandards;

        public TradeProfileManagerDialog(string? initialTradeCode = null)
        {
            Title = "Trade Profile Manager";
            Width = 1140;
            Height = 760;
            MinWidth = 960;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            UiTheme.ApplyTheme(this);

            var configMgr = TradeConfigManager.Instance;
            _activeTradeCode = configMgr.ActiveTradeCode ?? "TI0";

            foreach (var kvp in configMgr.Trades)
            {
                var cloned = kvp.Value.Clone();
                cloned.EnsureInitialized();
                _workingProfiles[kvp.Key] = cloned;
            }

            if (_workingProfiles.Count == 0)
            {
                var def = new TradeProfile();
                def.EnsureInitialized();
                _workingProfiles[def.TradeCode] = def;
            }

            if (!string.IsNullOrWhiteSpace(initialTradeCode) && _workingProfiles.ContainsKey(initialTradeCode!))
                _currentTradeCode = initialTradeCode;
            else if (!string.IsNullOrWhiteSpace(_activeTradeCode) && _workingProfiles.ContainsKey(_activeTradeCode))
                _currentTradeCode = _activeTradeCode;
            else
                _currentTradeCode = _workingProfiles.Keys.FirstOrDefault() ?? "TI0";

            var root = new DockPanel();
            Content = root;

            // ── TOP HEADER BAR ───────────────────────────────────────────────
            var headerBar = new Border
            {
                Background = UiTheme.SurfaceBrush,
                BorderBrush = UiTheme.BorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(18, 12, 18, 12)
            };
            DockPanel.SetDock(headerBar, Dock.Top);
            var headerDock = new DockPanel();
            headerBar.Child = headerDock;

            var headerActions = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(headerActions, Dock.Right);

            _headerActivePill = UiTheme.MakeStatusPill($"Active: {_activeTradeCode}", StatusTone.Info);
            _headerActivePill.Margin = new Thickness(0, 0, 12, 0);

            var btnImportJson = new WpfButton
            {
                Content = "Import JSON",
                Style = (Style)Resources["GhostButton"],
                Margin = new Thickness(0, 0, 4, 0)
            };
            btnImportJson.Click += (s, e) => OnImportJson();

            var btnExportJson = new WpfButton
            {
                Content = "Export JSON",
                Style = (Style)Resources["GhostButton"]
            };
            btnExportJson.Click += (s, e) => OnExportJson();

            headerActions.Children.Add(_headerActivePill);
            headerActions.Children.Add(btnImportJson);
            headerActions.Children.Add(btnExportJson);
            headerDock.Children.Add(headerActions);

            var headerTitleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headerTitleStack.Children.Add(new TextBlock
            {
                Text = "Trade Profile Manager",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextPrimaryBrush
            });
            headerTitleStack.Children.Add(new TextBlock
            {
                Text = "Configure discipline settings, synced Excel workbooks, parameter formulas, and export defaults",
                FontSize = 11.5,
                Foreground = UiTheme.TextMutedBrush,
                Margin = new Thickness(0, 2, 0, 0)
            });
            headerDock.Children.Add(headerTitleStack);
            root.Children.Add(headerBar);

            // ── BOTTOM FOOTER BAR ────────────────────────────────────────────
            var footerBar = new Border
            {
                Background = UiTheme.SurfaceBrush,
                BorderBrush = UiTheme.BorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(18, 10, 18, 10)
            };
            DockPanel.SetDock(footerBar, Dock.Bottom);
            var footerDock = new DockPanel();
            footerBar.Child = footerDock;

            var footerBtns = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            DockPanel.SetDock(footerBtns, Dock.Right);

            var btnSetActive = new WpfButton
            {
                Content = "Set as Active",
                Style = (Style)Resources["SecondaryButton"],
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Marks the currently selected profile as the active default trade."
            };
            btnSetActive.Click += (s, e) => OnSetActive();

            var btnSaveClose = new WpfButton
            {
                Content = "Save & Apply",
                IsDefault = true,
                Style = (Style)Resources["PrimaryButton"],
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 110
            };
            btnSaveClose.Click += (s, e) => OnSaveAndClose();

            var btnCancel = new WpfButton
            {
                Content = "Cancel",
                IsCancel = true,
                Style = (Style)Resources["SecondaryButton"],
                MinWidth = 84
            };
            btnCancel.Click += (s, e) => { DialogResult = false; };

            footerBtns.Children.Add(btnSetActive);
            footerBtns.Children.Add(btnSaveClose);
            footerBtns.Children.Add(btnCancel);
            footerDock.Children.Add(footerBtns);

            _txtFooterStatus = new TextBlock
            {
                Text = TradeConfigManager.GetConfigFilePath(),
                FontFamily = UiTheme.MonoFont,
                FontSize = 11,
                Foreground = UiTheme.TextMutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 16, 0)
            };
            footerDock.Children.Add(_txtFooterStatus);
            root.Children.Add(footerBar);

            // ── MAIN WORKSPACE (LEFT SIDEBAR + RIGHT TABBED EDITOR) ──────────
            var mainGrid = new Grid { Margin = new Thickness(14) };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(mainGrid);

            // ── LEFT SIDEBAR ─────────────────────────────────────────────────
            var leftCard = UiTheme.MakeCard(new Thickness(10), new Thickness(0));
            Grid.SetColumn(leftCard, 0);
            mainGrid.Children.Add(leftCard);

            var leftDock = new DockPanel();
            leftCard.Child = leftDock;

            var sideHeader = new TextBlock
            {
                Text = "PROFILES",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextMutedBrush,
                Margin = new Thickness(4, 2, 4, 8)
            };
            DockPanel.SetDock(sideHeader, Dock.Top);
            leftDock.Children.Add(sideHeader);

            var sideActions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            sideActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sideActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sideActions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sideActions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DockPanel.SetDock(sideActions, Dock.Bottom);

            var btnNew = new WpfButton { Content = "+ New", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 3, 6) };
            btnNew.Click += (s, e) => OnNewProfile();
            Grid.SetRow(btnNew, 0); Grid.SetColumn(btnNew, 0);
            sideActions.Children.Add(btnNew);

            var btnDup = new WpfButton { Content = "Duplicate", Style = (Style)Resources["SmallButton"], Margin = new Thickness(3, 0, 0, 6) };
            btnDup.Click += (s, e) => OnDuplicateProfile();
            Grid.SetRow(btnDup, 0); Grid.SetColumn(btnDup, 1);
            sideActions.Children.Add(btnDup);

            var btnDel = new WpfButton { Content = "Delete", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 3, 0) };
            btnDel.Foreground = UiTheme.DangerBrush;
            btnDel.Click += (s, e) => OnDeleteProfile();
            Grid.SetRow(btnDel, 1); Grid.SetColumn(btnDel, 0);
            sideActions.Children.Add(btnDel);

            var btnReset = new WpfButton { Content = "Reset Defaults", Style = (Style)Resources["SmallButton"], Margin = new Thickness(3, 0, 0, 0) };
            btnReset.Click += (s, e) => OnResetBuiltins();
            Grid.SetRow(btnReset, 1); Grid.SetColumn(btnReset, 1);
            sideActions.Children.Add(btnReset);

            leftDock.Children.Add(sideActions);

            _lbProfiles = new WpfListBox
            {
                BorderThickness = new Thickness(0),
                Background = WpfBrushes.Transparent,
                HorizontalContentAlignment = WpfHorizontalAlignment.Stretch
            };
            _lbProfiles.SelectionChanged += (s, e) =>
            {
                if (_isUpdatingEditor) return;
                SaveCurrentEditorToWorkingProfile();
                if (_lbProfiles.SelectedItem is ProfileListItem item)
                {
                    _currentTradeCode = item.TradeCode;
                    LoadWorkingProfileToEditor(item.TradeCode);
                }
            };
            leftDock.Children.Add(_lbProfiles);

            // ── RIGHT WORKSPACE (5 TABS) ─────────────────────────────────────
            var rightCard = UiTheme.MakeCard(new Thickness(0), new Thickness(0));
            Grid.SetColumn(rightCard, 2);
            mainGrid.Children.Add(rightCard);

            var tabs = new System.Windows.Controls.TabControl
            {
                Style = (Style)Resources["ModernTabControl"]
            };
            rightCard.Child = tabs;

            // ─── TAB 1: OVERVIEW & NAMING ────────────────────────────────────
            var tab1 = new TabItem { Header = "Overview & Naming", Style = (Style)Resources["ModernTabItem"] };
            var tab1Scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(18) };
            var tab1Body = new StackPanel();
            tab1Scroll.Content = tab1Body;
            tab1.Content = tab1Scroll;
            tabs.Items.Add(tab1);

            tab1Body.Children.Add(UiTheme.MakeSectionLabel("Trade Identity & Folder Structure",
                "Core discipline identifiers and folder segments used when reading and exporting models."));

            var idGrid = MakeFormGrid(160, 6);
            AddFormRow(idGrid, 0, "Trade Code:", _tbTradeCode = MakeTextBox(mono: true));
            AddFormRow(idGrid, 1, "Trade Name:", _tbTradeName = MakeTextBox());
            AddFormRow(idGrid, 2, "Discipline Folder:", _tbDisciplineFolder = MakeTextBox(mono: true));
            AddFormRow(idGrid, 3, "Work Package Folder:", _tbWorkPackageSubfolder = MakeTextBox());
            AddFormRow(idGrid, 4, "Project Number:", _tbProjectNumber = MakeTextBox(mono: true));
            AddFormRow(idGrid, 5, "Originator:", _tbOriginator = MakeTextBox(mono: true));
            tab1Body.Children.Add(idGrid);

            tab1Body.Children.Add(UiTheme.MakeSectionLabel("Output Filename Templates",
                "Available tokens: {ProjectNumber}, {Originator}, {TradeCode}, {Location}"));

            var fnGrid = MakeFormGrid(160, 3);
            AddFormRow(fnGrid, 0, "Central RVT:", _tbRvtTemplate = MakeTextBox(mono: true));
            AddFormRow(fnGrid, 1, "Export IFC:", _tbIfcTemplate = MakeTextBox(mono: true));
            AddFormRow(fnGrid, 2, "Export NWC:", _tbNwcTemplate = MakeTextBox(mono: true));
            tab1Body.Children.Add(fnGrid);

            var previewStrip = UiTheme.MakeCard(new Thickness(10, 8, 10, 8), new Thickness(0, 0, 0, 16), subtle: true);
            _txtTemplatePreview = new TextBlock
            {
                FontFamily = UiTheme.MonoFont,
                FontSize = 11.5,
                Foreground = UiTheme.TextSecondaryBrush,
                TextWrapping = TextWrapping.Wrap
            };
            previewStrip.Child = _txtTemplatePreview;
            tab1Body.Children.Add(previewStrip);

            _tbTradeCode.TextChanged += (s, e) => UpdateTemplatePreview();
            _tbProjectNumber.TextChanged += (s, e) => UpdateTemplatePreview();
            _tbOriginator.TextChanged += (s, e) => UpdateTemplatePreview();
            _tbRvtTemplate.TextChanged += (s, e) => UpdateTemplatePreview();
            _tbIfcTemplate.TextChanged += (s, e) => UpdateTemplatePreview();
            _tbNwcTemplate.TextChanged += (s, e) => UpdateTemplatePreview();

            tab1Body.Children.Add(UiTheme.MakeSectionLabel("External Configuration Paths",
                "Paths can use environment variables such as %USERPROFILE% or relative add-in subfolders."));

            var pathGrid = MakeFormGrid(160, 2);
            _tbIfcJsonPath = MakeTextBox(mono: true);
            var btnBrowseIfc = new WpfButton { Content = "Browse...", Style = (Style)Resources["SmallButton"], Margin = new Thickness(8, 0, 0, 0) };
            btnBrowseIfc.Click += (s, e) =>
            {
                var dlg = new MicrosoftOpenFileDialog { Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*", Title = "Select IFCSettings.json" };
                if (dlg.ShowDialog() == true) _tbIfcJsonPath.Text = dlg.FileName;
            };
            AddBrowseRow(pathGrid, 0, "IFC Settings JSON:", _tbIfcJsonPath, btnBrowseIfc);

            _tbModelCheckersFolder = MakeTextBox(mono: true);
            var btnBrowseMc = new WpfButton { Content = "Browse...", Style = (Style)Resources["SmallButton"], Margin = new Thickness(8, 0, 0, 0) };
            btnBrowseMc.Click += (s, e) =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select ModelCheckers Root Folder" };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    _tbModelCheckersFolder.Text = dlg.SelectedPath;
            };
            AddBrowseRow(pathGrid, 1, "ModelCheckers Folder:", _tbModelCheckersFolder, btnBrowseMc);
            tab1Body.Children.Add(pathGrid);

            // ─── TAB 2: EXCEL SYNC ───────────────────────────────────────────
            var tab2 = new TabItem { Header = "Excel Sync", Style = (Style)Resources["ModernTabItem"] };
            var tab2Scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(18) };
            var tab2Body = new StackPanel();
            tab2Scroll.Content = tab2Body;
            tab2.Content = tab2Scroll;
            tabs.Items.Add(tab2);

            tab2Body.Children.Add(UiTheme.MakeSectionLabel("Linked Master Workbook",
                "Sync this Trade Profile directly with an Excel workbook so Location Exporter runs without prompting for a file every time."));

            var excelCard = UiTheme.MakeCard(new Thickness(14), new Thickness(0, 0, 0, 14), subtle: true);
            var excelStack = new StackPanel();
            excelCard.Child = excelStack;

            var excelFileGrid = MakeFormGrid(130, 1);
            excelFileGrid.Margin = new Thickness(0, 0, 0, 10);
            _tbLinkedExcelPath = MakeTextBox(mono: true);
            var btnBrowseExcel = new WpfButton { Content = "Browse...", Style = (Style)Resources["SmallButton"], Margin = new Thickness(8, 0, 0, 0) };
            btnBrowseExcel.Click += (s, e) => OnBrowseLinkedExcel();
            AddBrowseRow(excelFileGrid, 0, "Excel Workbook:", _tbLinkedExcelPath, btnBrowseExcel);
            excelStack.Children.Add(excelFileGrid);

            _cbAutoSyncExcel = new WpfCheckBox
            {
                Content = "Auto-sync automatically before running exports if the Excel file was modified",
                Style = (Style)Resources["ModernCheckBox"],
                Margin = new Thickness(0, 0, 0, 12)
            };
            excelStack.Children.Add(_cbAutoSyncExcel);

            var syncActionDock = new DockPanel();
            var syncBtnStack = new StackPanel { Orientation = WpfOrientation.Horizontal };
            DockPanel.SetDock(syncBtnStack, Dock.Left);

            var btnSyncNow = new WpfButton { Content = "Sync Now", Style = (Style)Resources["PrimaryButton"], Margin = new Thickness(0, 0, 8, 0) };
            btnSyncNow.Click += (s, e) => OnSyncNow();

            var btnExportTemplate = new WpfButton { Content = "Export Template .xlsx", Style = (Style)Resources["SecondaryButton"], Margin = new Thickness(0, 0, 8, 0) };
            btnExportTemplate.Click += (s, e) => OnExportExcelTemplate();

            var btnOpenExcel = new WpfButton { Content = "Open Excel", Style = (Style)Resources["GhostButton"] };
            btnOpenExcel.Click += (s, e) => OnOpenLinkedExcel();

            syncBtnStack.Children.Add(btnSyncNow);
            syncBtnStack.Children.Add(btnExportTemplate);
            syncBtnStack.Children.Add(btnOpenExcel);
            syncActionDock.Children.Add(syncBtnStack);

            var statusRight = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _syncStatusPill = UiTheme.MakeStatusPill("Not linked", StatusTone.Neutral);
            _txtSyncTimestamp = new TextBlock
            {
                FontSize = 11.5,
                Foreground = UiTheme.TextMutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            statusRight.Children.Add(_syncStatusPill);
            statusRight.Children.Add(_txtSyncTimestamp);
            syncActionDock.Children.Add(statusRight);
            excelStack.Children.Add(syncActionDock);
            tab2Body.Children.Add(excelCard);

            tab2Body.Children.Add(UiTheme.MakeSectionLabel("Synced Snapshot Summary",
                "Data cached inside this Trade Profile from the last successful workbook sync."));

            var metricsGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            for (int i = 0; i < 5; i++)
                metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tileElem = UiTheme.MakeMetricTile("Element Locations", out _txtMetricElem);
            Grid.SetColumn(tileElem, 0); metricsGrid.Children.Add(tileElem);

            var tileSheet = UiTheme.MakeMetricTile("Sheet Locations", out _txtMetricSheet);
            Grid.SetColumn(tileSheet, 1); metricsGrid.Children.Add(tileSheet);

            var tileSpec = UiTheme.MakeMetricTile("Sheet Specific", out _txtMetricSpecific);
            Grid.SetColumn(tileSpec, 2); metricsGrid.Children.Add(tileSpec);

            var tileStd = UiTheme.MakeMetricTile("Standards Fill", out _txtMetricStandards);
            Grid.SetColumn(tileStd, 3); metricsGrid.Children.Add(tileStd);

            var tileExp = UiTheme.MakeMetricTile("Export Folders", out _txtMetricExports);
            tileExp.Margin = new Thickness(0);
            Grid.SetColumn(tileExp, 4); metricsGrid.Children.Add(tileExp);

            tab2Body.Children.Add(metricsGrid);

            tab2Body.Children.Add(UiTheme.MakeSectionLabel("Available Excel Columns for Formulas",
                "Click any column chip to copy its [ColumnName] token to the clipboard for use in parameter formulas."));

            _excelColumnsPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            tab2Body.Children.Add(_excelColumnsPanel);

            // ─── TAB 3: PARAMETERS & FORMULAS ────────────────────────────────
            var tab3 = new TabItem { Header = "Parameters & Formulas", Style = (Style)Resources["ModernTabItem"] };
            var tab3Dock = new DockPanel { Margin = new Thickness(18) };
            tab3.Content = tab3Dock;
            tabs.Items.Add(tab3);

            var rulesHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(rulesHeaderDock, Dock.Top);

            var rulesBtns = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(rulesBtns, Dock.Right);

            var btnAddRule = new WpfButton { Content = "+ Add Rule", Style = (Style)Resources["PrimaryButton"], Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
            btnAddRule.Click += (s, e) => OnAddRule();

            var btnDupRule = new WpfButton { Content = "Duplicate", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 6, 0) };
            btnDupRule.Click += (s, e) => OnDuplicateRule();

            var btnDelRule = new WpfButton { Content = "Remove", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 6, 0) };
            btnDelRule.Foreground = UiTheme.DangerBrush;
            btnDelRule.Click += (s, e) => OnRemoveRule();

            var btnRuleUp = new WpfButton { Content = "Up", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 4, 0) };
            btnRuleUp.Click += (s, e) => MoveSelectedRule(-1);

            var btnRuleDown = new WpfButton { Content = "Down", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 8, 0) };
            btnRuleDown.Click += (s, e) => MoveSelectedRule(1);

            var btnSyntaxHelp = new WpfButton { Content = "Syntax Help", Style = (Style)Resources["GhostButton"] };
            btnSyntaxHelp.Click += (s, e) => ShowFormulaSyntaxDialog();

            rulesBtns.Children.Add(btnAddRule);
            rulesBtns.Children.Add(btnDupRule);
            rulesBtns.Children.Add(btnDelRule);
            rulesBtns.Children.Add(btnRuleUp);
            rulesBtns.Children.Add(btnRuleDown);
            rulesBtns.Children.Add(btnSyntaxHelp);
            rulesHeaderDock.Children.Add(rulesBtns);

            rulesHeaderDock.Children.Add(UiTheme.MakeSectionLabel("Parameter Rules & Dependent Formulas",
                "Prefix values with '=' to compute from other parameters via [ParamName] in automatic dependency order."));
            tab3Dock.Children.Add(rulesHeaderDock);

            // Live Formula Tester docked at bottom of Tab 3
            var testerCard = UiTheme.MakeCard(new Thickness(12, 10, 12, 10), new Thickness(0, 10, 0, 0), subtle: true);
            DockPanel.SetDock(testerCard, Dock.Bottom);
            var testerStack = new StackPanel();
            testerCard.Child = testerStack;

            var testerTop = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var lblTester = new TextBlock
            {
                Text = "Live Formula Preview",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = UiTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            DockPanel.SetDock(lblTester, Dock.Left);
            testerTop.Children.Add(lblTester);

            _txtFormulaStatus = new TextBlock
            {
                FontSize = 11.5,
                Foreground = UiTheme.TextMutedBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            DockPanel.SetDock(_txtFormulaStatus, Dock.Right);
            testerTop.Children.Add(_txtFormulaStatus);

            _cbSampleLocator = new WpfComboBox
            {
                Style = (Style)Resources["ModernComboBox"],
                Width = 220,
                HorizontalAlignment = WpfHorizontalAlignment.Left
            };
            _cbSampleLocator.SelectionChanged += (s, e) =>
            {
                if (_isUpdatingEditor) return;
                LoadSampleInputsForSelectedLocator();
                RefreshFormulaPreviews();
            };
            testerTop.Children.Add(_cbSampleLocator);
            testerStack.Children.Add(testerTop);

            var sampleRow = new DockPanel();
            var lblSample = new TextBlock
            {
                Text = "Sample Inputs:",
                FontSize = 11.5,
                Foreground = UiTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(lblSample, Dock.Left);
            sampleRow.Children.Add(lblSample);

            _tbSampleInputs = new WpfTextBox
            {
                Style = (Style)Resources["MonoTextBox"],
                Text = "CPK_WBS_5=C.A.AI.NVL.10.CON.81; para1=shiv.mahto",
                ToolTip = "Semicolon-separated Key=Value pairs for live preview testing."
            };
            _tbSampleInputs.TextChanged += (s, e) => RefreshFormulaPreviews();
            sampleRow.Children.Add(_tbSampleInputs);
            testerStack.Children.Add(sampleRow);
            tab3Dock.Children.Add(testerCard);

            // DataGrid for Parameter Rules
            _dgRules = new WpfDataGrid
            {
                Style = (Style)Resources["ModernDataGrid"],
                ItemsSource = _ruleRows
            };

            _dgRules.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "On",
                Width = 40,
                Binding = new WpfBinding("Enabled") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            _dgRules.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Target",
                Width = 100,
                ItemsSource = Enum.GetValues(typeof(ParameterRuleTarget)),
                SelectedItemBinding = new WpfBinding("Target") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            _dgRules.Columns.Add(new DataGridTextColumn
            {
                Header = "Filter",
                Width = 115,
                Binding = new WpfBinding("Filter") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });

            var paramColStyle = new Style(typeof(TextBlock));
            paramColStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, UiTheme.MonoFont));
            paramColStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.0));

            _dgRules.Columns.Add(new DataGridTextColumn
            {
                Header = "Parameter",
                Width = 155,
                ElementStyle = paramColStyle,
                Binding = new WpfBinding("ParameterName") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });

            var formulaColStyle = new Style(typeof(TextBlock));
            formulaColStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, UiTheme.MonoFont));
            formulaColStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.5));

            _dgRules.Columns.Add(new DataGridTextColumn
            {
                Header = "Value / Formula",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = formulaColStyle,
                Binding = new WpfBinding("ValueOrFormula") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });
            _dgRules.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Mode",
                Width = 100,
                ItemsSource = Enum.GetValues(typeof(ParameterFillMode)),
                SelectedItemBinding = new WpfBinding("Mode") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });

            // Read-only Preview column with semantic green/red styling
            var previewTemplate = new DataTemplate();
            var previewFactory = new FrameworkElementFactory(typeof(TextBlock));
            previewFactory.SetBinding(TextBlock.TextProperty, new WpfBinding("PreviewText"));
            previewFactory.SetBinding(TextBlock.ForegroundProperty, new WpfBinding("PreviewBrush"));
            previewFactory.SetValue(TextBlock.FontFamilyProperty, UiTheme.MonoFont);
            previewFactory.SetValue(TextBlock.FontSizeProperty, 11.5);
            previewFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            previewFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            previewFactory.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding("PreviewText"));
            previewTemplate.VisualTree = previewFactory;

            _dgRules.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Preview",
                Width = 175,
                IsReadOnly = true,
                CellTemplate = previewTemplate
            });

            _dgRules.CellEditEnding += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() => RefreshFormulaPreviews()));
            };
            tab3Dock.Children.Add(_dgRules);

            // ─── TAB 3B: SHEET TEXT NOTES ────────────────────────────────────
            var tabTn = new TabItem { Header = "Sheet Text Notes", Style = (Style)Resources["ModernTabItem"] };
            var tabTnDock = new DockPanel { Margin = new Thickness(18) };
            tabTn.Content = tabTnDock;
            tabs.Items.Add(tabTn);

            var tnHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(tnHeaderDock, Dock.Top);

            var tnBtns = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(tnBtns, Dock.Right);

            var btnAddTn = new WpfButton { Content = "+ Add Rule", Style = (Style)Resources["PrimaryButton"], Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0) };
            btnAddTn.Click += (s, e) => OnAddTextNoteRule();

            var btnDupTn = new WpfButton { Content = "Duplicate", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 6, 0) };
            btnDupTn.Click += (s, e) => OnDuplicateTextNoteRule();

            var btnDelTn = new WpfButton { Content = "Remove", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 6, 0) };
            btnDelTn.Foreground = UiTheme.DangerBrush;
            btnDelTn.Click += (s, e) => OnRemoveTextNoteRule();

            var btnPresetTiu = new WpfButton { Content = "Add TIU Inert Gas Preset", Style = (Style)Resources["SecondaryButton"], Padding = new Thickness(10, 5, 10, 5) };
            btnPresetTiu.Click += (s, e) => OnAddTiuPresetRule();

            tnBtns.Children.Add(btnAddTn);
            tnBtns.Children.Add(btnDupTn);
            tnBtns.Children.Add(btnDelTn);
            tnBtns.Children.Add(btnPresetTiu);
            tnHeaderDock.Children.Add(tnBtns);
            tnHeaderDock.Children.Add(UiTheme.MakeSectionLabel("Sheet & View TextNote Replacement Rules",
                "Opt-in find/replace rules for TextNotes (e.g. Mechanical drawing locators on Telecom TIU Fire sheets). Supports [Location], [ProjectNumber], [Originator], and '=' formulas."));
            tabTnDock.Children.Add(tnHeaderDock);

            // Live TextNote Preview card docked at bottom
            var tnPreviewCard = UiTheme.MakeCard(new Thickness(12, 10, 12, 10), new Thickness(0, 10, 0, 0), subtle: true);
            DockPanel.SetDock(tnPreviewCard, Dock.Bottom);
            var tnPrevStack = new StackPanel();
            tnPreviewCard.Child = tnPrevStack;

            var tnPrevTop = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _txtTnStatus = new TextBlock
            {
                FontSize = 11.5,
                Foreground = UiTheme.TextMutedBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtTnStatus, Dock.Right);
            tnPrevTop.Children.Add(_txtTnStatus);
            tnPrevTop.Children.Add(new TextBlock
            {
                Text = "Live TextNote Preview",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            tnPrevStack.Children.Add(tnPrevTop);

            var tnCtxGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            tnCtxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            tnCtxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tnCtxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            tnCtxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

            var lblSh = UiTheme.MakeFieldLabel("Sample Sheet:");
            Grid.SetColumn(lblSh, 0); tnCtxGrid.Children.Add(lblSh);
            _tbTnSampleSheet = new WpfTextBox
            {
                Style = (Style)Resources["MonoTextBox"],
                Text = "P00100087-DAR-TIU-AFNXIEELS-GF-DR-240001_PL",
                Margin = new Thickness(0, 0, 12, 0)
            };
            _tbTnSampleSheet.TextChanged += (s, e) => RefreshTextNotePreview();
            Grid.SetColumn(_tbTnSampleSheet, 1); tnCtxGrid.Children.Add(_tbTnSampleSheet);

            var lblLoc = UiTheme.MakeFieldLabel("Target Location:");
            Grid.SetColumn(lblLoc, 2); tnCtxGrid.Children.Add(lblLoc);
            _tbTnSampleLocator = new WpfTextBox
            {
                Style = (Style)Resources["MonoTextBox"],
                Text = "AFNXIEELS"
            };
            _tbTnSampleLocator.TextChanged += (s, e) => RefreshTextNotePreview();
            Grid.SetColumn(_tbTnSampleLocator, 3); tnCtxGrid.Children.Add(_tbTnSampleLocator);
            tnPrevStack.Children.Add(tnCtxGrid);

            var tnSplitGrid = new Grid();
            tnSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tnSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            tnSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var inCol = new StackPanel();
            inCol.Children.Add(new TextBlock { Text = "Sample TextNote (Before)", FontSize = 11, Foreground = UiTheme.TextMutedBrush, Margin = new Thickness(0, 0, 0, 3) });
            _tbTnSampleInput = new WpfTextBox
            {
                Style = (Style)Resources["MonoTextBox"],
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(8, 6, 8, 6),
                Height = 76,
                Text = "ZAKRESKOWANE POMIESZCZENIA SĄ CHRONIONE INSTALACJĄ\r\nGASZENIA GAZEM „INERT GAS SYSTEM”. DOKŁADNE INFORMACJE\r\nPATRZ RYSUNEK BRANŻY MECHANICZNEJ NR\r\nP00100087-DAR-MIF-LFEXREECS-GF-DR-314001_PL"
            };
            _tbTnSampleInput.TextChanged += (s, e) => RefreshTextNotePreview();
            inCol.Children.Add(_tbTnSampleInput);
            Grid.SetColumn(inCol, 0); tnSplitGrid.Children.Add(inCol);

            var outCol = new StackPanel();
            outCol.Children.Add(new TextBlock { Text = "Result After Rules (After)", FontSize = 11, Foreground = UiTheme.TextMutedBrush, Margin = new Thickness(0, 0, 0, 3) });
            _tbTnSampleOutput = new WpfTextBox
            {
                Style = (Style)Resources["MonoTextBox"],
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(8, 6, 8, 6),
                Height = 76
            };
            outCol.Children.Add(_tbTnSampleOutput);
            Grid.SetColumn(outCol, 2); tnSplitGrid.Children.Add(outCol);
            tnPrevStack.Children.Add(tnSplitGrid);
            tabTnDock.Children.Add(tnPreviewCard);

            _dgTextNoteRules = new WpfDataGrid
            {
                Style = (Style)Resources["ModernDataGrid"],
                ItemsSource = _textNoteRows
            };
            _dgTextNoteRules.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "On",
                Width = 40,
                Binding = new WpfBinding("Enabled") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            _dgTextNoteRules.Columns.Add(new DataGridTextColumn
            {
                Header = "Sheet Filter",
                Width = 100,
                ElementStyle = paramColStyle,
                Binding = new WpfBinding("SheetFilter") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });
            _dgTextNoteRules.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Scope",
                Width = 170,
                ItemsSource = Enum.GetValues(typeof(TextNoteScope)),
                SelectedItemBinding = new WpfBinding("Scope") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            _dgTextNoteRules.Columns.Add(new DataGridTextColumn
            {
                Header = "Note Contains",
                Width = 140,
                Binding = new WpfBinding("NoteContains") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });
            _dgTextNoteRules.Columns.Add(new DataGridTextColumn
            {
                Header = "Find Pattern",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = formulaColStyle,
                Binding = new WpfBinding("FindPattern") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });
            _dgTextNoteRules.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Regex",
                Width = 52,
                Binding = new WpfBinding("IsRegex") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            _dgTextNoteRules.Columns.Add(new DataGridTextColumn
            {
                Header = "Replace With",
                Width = new DataGridLength(1.1, DataGridLengthUnitType.Star),
                ElementStyle = formulaColStyle,
                Binding = new WpfBinding("ReplaceWith") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });
            _dgTextNoteRules.CellEditEnding += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() => RefreshTextNotePreview()));
            };
            tabTnDock.Children.Add(_dgTextNoteRules);

            // ─── TAB 4: CATEGORIES & WORKSETS ────────────────────────────────
            var tab4 = new TabItem { Header = "Categories & Worksets", Style = (Style)Resources["ModernTabItem"] };
            var tab4Dock = new DockPanel { Margin = new Thickness(18) };
            tab4.Content = tab4Dock;
            tabs.Items.Add(tab4);

            var wsSection = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(wsSection, Dock.Top);
            wsSection.Children.Add(UiTheme.MakeSectionLabel("Workset Match & Rename Rules",
                "Comma-separated workset tokens to open and suffixes renamed per location."));
            var wsGrid = MakeFormGrid(160, 2);
            AddFormRow(wsGrid, 0, "Workset Match Tokens:", _tbMatchTokens = MakeTextBox(mono: true));
            AddFormRow(wsGrid, 1, "Workset Rename Suffixes:", _tbWorksetSuffixes = MakeTextBox(mono: true));
            wsSection.Children.Add(wsGrid);
            tab4Dock.Children.Add(wsSection);

            var catHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(catHeaderDock, Dock.Top);
            _txtCategoryCount = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtCategoryCount, Dock.Right);
            catHeaderDock.Children.Add(_txtCategoryCount);
            catHeaderDock.Children.Add(UiTheme.MakeSectionLabel("Target Revit Categories",
                "BuiltInCategories targeted when populating Element parameters."));
            tab4Dock.Children.Add(catHeaderDock);

            var catToolDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(catToolDock, Dock.Top);
            var catToolBtns = new StackPanel { Orientation = WpfOrientation.Horizontal };
            DockPanel.SetDock(catToolBtns, Dock.Right);
            var btnSelAllCats = new WpfButton { Content = "Select All", Style = (Style)Resources["SmallButton"], Margin = new Thickness(8, 0, 4, 0) };
            btnSelAllCats.Click += (s, e) => SetAllCategories(true);
            var btnClrCats = new WpfButton { Content = "Clear", Style = (Style)Resources["SmallButton"] };
            btnClrCats.Click += (s, e) => SetAllCategories(false);
            catToolBtns.Children.Add(btnSelAllCats);
            catToolBtns.Children.Add(btnClrCats);
            catToolDock.Children.Add(catToolBtns);

            _tbCategorySearch = MakeTextBox();
            _tbCategorySearch.ToolTip = "Filter BuiltInCategory names...";
            _tbCategorySearch.TextChanged += (s, e) => RefreshCategoriesList();
            catToolDock.Children.Add(_tbCategorySearch);
            tab4Dock.Children.Add(catToolDock);

            var customCatDock = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(customCatDock, Dock.Bottom);
            var btnAddCustomCat = new WpfButton { Content = "Add Category", Style = (Style)Resources["SecondaryButton"], Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0) };
            btnAddCustomCat.Click += (s, e) => OnAddCustomCategory();
            DockPanel.SetDock(btnAddCustomCat, Dock.Right);
            customCatDock.Children.Add(btnAddCustomCat);
            _tbCustomCategory = MakeTextBox(mono: true);
            _tbCustomCategory.ToolTip = "Enter an exact BuiltInCategory name (e.g. OST_CableTray)";
            customCatDock.Children.Add(_tbCustomCategory);
            tab4Dock.Children.Add(customCatDock);

            var catBorder = UiTheme.MakeCard(new Thickness(6), new Thickness(0));
            var catScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _categoriesListStack = new StackPanel();
            catScroll.Content = _categoriesListStack;
            catBorder.Child = catScroll;
            tab4Dock.Children.Add(catBorder);

            // ─── TAB 5: PROJECT, SHEETS & DEFAULTS ───────────────────────────
            var tab5 = new TabItem { Header = "Project, Sheets & Defaults", Style = (Style)Resources["ModernTabItem"] };
            var tab5Scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(18) };
            var tab5Body = new StackPanel();
            tab5Scroll.Content = tab5Body;
            tab5.Content = tab5Scroll;
            tabs.Items.Add(tab5);

            // Survey & Sheet Rules two-column top grid
            var topTab5Grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            topTab5Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topTab5Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            topTab5Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var surveyPanel = new StackPanel();
            surveyPanel.Children.Add(UiTheme.MakeSectionLabel("Shared Survey Point (mm)", "Coordinates written when aligning location."));
            var svGrid = MakeFormGrid(105, 3);
            AddFormRow(svGrid, 0, "North (N):", _tbSurveyN = MakeTextBox(mono: true));
            AddFormRow(svGrid, 1, "East (E):", _tbSurveyE = MakeTextBox(mono: true));
            AddFormRow(svGrid, 2, "Elevation (Z):", _tbSurveyZ = MakeTextBox(mono: true));
            surveyPanel.Children.Add(svGrid);
            Grid.SetColumn(surveyPanel, 0);
            topTab5Grid.Children.Add(surveyPanel);

            var sheetPanel = new StackPanel();
            sheetPanel.Children.Add(UiTheme.MakeSectionLabel("Sheet Filtering & Sync Rules", "Controls which template sheets are updated, deleted, and exported."));
            var shGrid = MakeFormGrid(125, 3);
            AddFormRow(shGrid, 0, "Include Token:", _tbSheetIncludeToken = MakeTextBox(mono: true));
            AddFormRow(shGrid, 1, "Delete Patterns:", _tbSheetDeletePatterns = MakeTextBox(mono: true));
            AddFormRow(shGrid, 2, "Sheet Cover Name:", _tbSheetCoverName = MakeTextBox());
            sheetPanel.Children.Add(shGrid);
            _cbSyncWpCheckboxes = new WpfCheckBox
            {
                Content = "Sync WPxx Yes/No checkboxes on titleblocks",
                Style = (Style)Resources["ModernCheckBox"],
                Margin = new Thickness(0, 2, 0, 0)
            };
            sheetPanel.Children.Add(_cbSyncWpCheckboxes);
            Grid.SetColumn(sheetPanel, 2);
            topTab5Grid.Children.Add(sheetPanel);
            tab5Body.Children.Add(topTab5Grid);

            _dgProjectInfo = new WpfDataGrid
            {
                Style = (Style)Resources["ModernDataGrid"],
                ItemsSource = _projectInfoRows,
                Height = 190,
                Margin = new Thickness(0, 0, 0, 16)
            };

            // ProjectInfo Key/Value Grid
            var piHeaderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var piBtns = new StackPanel { Orientation = WpfOrientation.Horizontal };
            DockPanel.SetDock(piBtns, Dock.Right);
            var btnAddPi = new WpfButton { Content = "+ Add Field", Style = (Style)Resources["SmallButton"], Margin = new Thickness(0, 0, 6, 0) };
            btnAddPi.Click += (s, e) => _projectInfoRows.Add(new ProjectInfoEntryVm { Key = "New_Parameter", Value = "" });
            var btnRemPi = new WpfButton { Content = "Remove", Style = (Style)Resources["SmallButton"] };
            btnRemPi.Foreground = UiTheme.DangerBrush;
            btnRemPi.Click += (s, e) =>
            {
                if (_dgProjectInfo.SelectedItem is ProjectInfoEntryVm row)
                    _projectInfoRows.Remove(row);
            };
            piBtns.Children.Add(btnAddPi);
            piBtns.Children.Add(btnRemPi);
            piHeaderDock.Children.Add(piBtns);
            piHeaderDock.Children.Add(UiTheme.MakeSectionLabel("Project Information Overrides",
                "Values applied to Revit Project Information for this trade."));
            tab5Body.Children.Add(piHeaderDock);
            _dgProjectInfo.Columns.Add(new DataGridTextColumn
            {
                Header = "Project Information Parameter",
                Width = 230,
                ElementStyle = paramColStyle,
                Binding = new WpfBinding("Key") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });
            _dgProjectInfo.Columns.Add(new DataGridTextColumn
            {
                Header = "Value",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                Binding = new WpfBinding("Value") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }
            });
            tab5Body.Children.Add(_dgProjectInfo);

            // Trade Default Execution Options
            tab5Body.Children.Add(UiTheme.MakeSectionLabel("Default Run Operations for This Trade",
                "Automatically pre-selected in Location Exporter when this trade is active."));
            var defOpsGrid = new Grid();
            for (int c = 0; c < 3; c++) defOpsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var c1 = new StackPanel();
            c1.Children.Add(_cbDefFillParams = MakeCheck("Fill CPK & Sheet Params"));
            c1.Children.Add(_cbDefCoordWorksets = MakeCheck("Coordinates & Worksets"));
            c1.Children.Add(_cbDefPurge = MakeCheck("Purge Unused Elements"));
            Grid.SetColumn(c1, 0); defOpsGrid.Children.Add(c1);

            var c2 = new StackPanel();
            c2.Children.Add(_cbDefNwc = MakeCheck("Export NWC (.nwc)"));
            c2.Children.Add(_cbDefIfc = MakeCheck("Export IFC (.ifc)"));
            c2.Children.Add(_cbDefRvt = MakeCheck("Save Central RVT (.rvt)"));
            Grid.SetColumn(c2, 1); defOpsGrid.Children.Add(c2);

            var c3 = new StackPanel();
            c3.Children.Add(_cbDefPdf = MakeCheck("Export Sheet PDF (.pdf)"));
            c3.Children.Add(_cbDefDwg = MakeCheck("Export Sheet DWG (.dwg)"));
            c3.Children.Add(_cbDefStandards = MakeCheck("Model Checker Standards"));
            Grid.SetColumn(c3, 2); defOpsGrid.Children.Add(c3);

            tab5Body.Children.Add(defOpsGrid);

            // Initial load
            RefreshProfilesListBox();
            if (!string.IsNullOrEmpty(_currentTradeCode))
                LoadWorkingProfileToEditor(_currentTradeCode!);
        }

        // ── Layout Builders ──────────────────────────────────────────────────

        private WpfTextBox MakeTextBox(bool mono = false) => new()
        {
            Style = (Style)Resources[mono ? "MonoTextBox" : "ModernTextBox"],
            Margin = new Thickness(0, 0, 0, 6)
        };

        private WpfCheckBox MakeCheck(string label) => new()
        {
            Content = label,
            Style = (Style)Resources["ModernCheckBox"],
            Margin = new Thickness(0, 0, 0, 6)
        };

        private static Grid MakeFormGrid(double labelWidth, int rows)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < rows; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return g;
        }

        private static void AddFormRow(Grid grid, int row, string label, UIElement control)
        {
            var lbl = UiTheme.MakeFieldLabel(label);
            lbl.Margin = new Thickness(0, 0, 10, 6);
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            Grid.SetRow(control, row); Grid.SetColumn(control, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(control);
        }

        private static void AddBrowseRow(Grid grid, int row, string label, WpfTextBox tb, WpfButton btn)
        {
            var lbl = UiTheme.MakeFieldLabel(label);
            lbl.Margin = new Thickness(0, 0, 10, 6);
            Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            tb.Margin = new Thickness(0);
            btn.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(btn, Dock.Right);
            dock.Children.Add(btn);
            dock.Children.Add(tb);
            Grid.SetRow(dock, row); Grid.SetColumn(dock, 1);
            grid.Children.Add(dock);
        }

        // ── Profile List & Load/Save ─────────────────────────────────────────

        private void RefreshProfilesListBox()
        {
            _isUpdatingEditor = true;
            _lbProfiles.Items.Clear();

            foreach (var profile in _workingProfiles.Values.OrderBy(p => p.TradeCode, StringComparer.OrdinalIgnoreCase))
            {
                bool isActive = string.Equals(profile.TradeCode, _activeTradeCode, StringComparison.OrdinalIgnoreCase);
                _lbProfiles.Items.Add(new ProfileListItem(profile, isActive));
            }

            foreach (ProfileListItem item in _lbProfiles.Items)
            {
                if (string.Equals(item.TradeCode, _currentTradeCode, StringComparison.OrdinalIgnoreCase))
                {
                    _lbProfiles.SelectedItem = item;
                    break;
                }
            }

            UiTheme.UpdateStatusPill(_headerActivePill, $"Active: {_activeTradeCode}", StatusTone.Info);
            _isUpdatingEditor = false;
        }

        private void LoadWorkingProfileToEditor(string tradeCode)
        {
            if (!_workingProfiles.TryGetValue(tradeCode, out var p)) return;
            p.EnsureInitialized();

            _isUpdatingEditor = true;
            try
            {
                _tbTradeCode.Text = p.TradeCode;
                _tbTradeName.Text = p.TradeName;
                _tbDisciplineFolder.Text = p.DisciplineFolder;
                _tbWorkPackageSubfolder.Text = p.WorkPackageSubfolder;
                _tbProjectNumber.Text = p.ProjectNumber;
                _tbOriginator.Text = p.Originator;

                _tbRvtTemplate.Text = p.RvtFileNameTemplate;
                _tbIfcTemplate.Text = p.IfcFileNameTemplate;
                _tbNwcTemplate.Text = p.NwcFileNameTemplate;
                _tbIfcJsonPath.Text = p.IFCJsonPath;
                _tbModelCheckersFolder.Text = p.ModelCheckersFolder;

                _tbLinkedExcelPath.Text = p.LinkedExcelPath ?? string.Empty;
                _cbAutoSyncExcel.IsChecked = p.AutoSyncExcelOnRun;

                _tbMatchTokens.Text = string.Join(", ", p.WorksetMatchTokens);
                _tbWorksetSuffixes.Text = string.Join(", ", p.WorksetSuffixes);

                _selectedCategories.Clear();
                foreach (string cat in p.TargetCategoryNames)
                {
                    if (!string.IsNullOrWhiteSpace(cat))
                        _selectedCategories.Add(cat.Trim());
                }
                RefreshCategoriesList();

                _ruleRows.Clear();
                foreach (var r in p.ParameterRules)
                    _ruleRows.Add(ParameterRuleRowVm.FromRule(r));

                _textNoteRows.Clear();
                foreach (var r in p.TextNoteRules)
                    _textNoteRows.Add(TextNoteRuleRowVm.FromRule(r));

                _tbSurveyN.Text = p.SurveyNorthMm.ToString(CultureInfo.InvariantCulture);
                _tbSurveyE.Text = p.SurveyEastMm.ToString(CultureInfo.InvariantCulture);
                _tbSurveyZ.Text = p.SurveyElevMm.ToString(CultureInfo.InvariantCulture);
                _tbSheetIncludeToken.Text = p.SheetIncludeToken;
                _tbSheetDeletePatterns.Text = string.Join(", ", p.SheetDeletePatterns);
                _tbSheetCoverName.Text = p.SheetCoverName;
                _cbSyncWpCheckboxes.IsChecked = p.SyncWorkPackageCheckboxes;

                _projectInfoRows.Clear();
                foreach (var kv in p.ProjectInfoData)
                    _projectInfoRows.Add(new ProjectInfoEntryVm { Key = kv.Key, Value = kv.Value });

                _cbDefFillParams.IsChecked = p.DefaultFillParameters;
                _cbDefCoordWorksets.IsChecked = p.DefaultAlignCoordinatesAndWorksets;
                _cbDefPurge.IsChecked = p.DefaultPurgeUnused;
                _cbDefNwc.IsChecked = p.DefaultExportNwc;
                _cbDefIfc.IsChecked = p.DefaultExportIfc;
                _cbDefRvt.IsChecked = p.DefaultSaveCentralRvt;
                _cbDefPdf.IsChecked = p.DefaultExportPdf;
                _cbDefDwg.IsChecked = p.DefaultExportDwg;
                _cbDefStandards.IsChecked = p.DefaultEnforceModelStandards;

                UpdateTemplatePreview();
                RefreshExcelSyncTab(p);
                PopulateSampleLocatorDropdown(p);
                RefreshFormulaPreviews();
                RefreshTextNotePreview();
            }
            finally
            {
                _isUpdatingEditor = false;
            }
        }

        private void SaveCurrentEditorToWorkingProfile()
        {
            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var p))
                return;

            _dgRules.CommitEdit(DataGridEditingUnit.Row, true);
            _dgTextNoteRules.CommitEdit(DataGridEditingUnit.Row, true);
            _dgProjectInfo.CommitEdit(DataGridEditingUnit.Row, true);

            string newCode = (_tbTradeCode.Text ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(newCode)) newCode = p.TradeCode;

            p.TradeName = (_tbTradeName.Text ?? string.Empty).Trim();
            p.DisciplineFolder = (_tbDisciplineFolder.Text ?? string.Empty).Trim();
            p.WorkPackageSubfolder = (_tbWorkPackageSubfolder.Text ?? string.Empty).Trim();
            p.ProjectNumber = (_tbProjectNumber.Text ?? string.Empty).Trim();
            p.Originator = (_tbOriginator.Text ?? string.Empty).Trim();

            p.RvtFileNameTemplate = (_tbRvtTemplate.Text ?? string.Empty).Trim();
            p.IfcFileNameTemplate = (_tbIfcTemplate.Text ?? string.Empty).Trim();
            p.NwcFileNameTemplate = (_tbNwcTemplate.Text ?? string.Empty).Trim();
            p.IFCJsonPath = (_tbIfcJsonPath.Text ?? string.Empty).Trim();
            p.ModelCheckersFolder = (_tbModelCheckersFolder.Text ?? string.Empty).Trim();

            p.LinkedExcelPath = (_tbLinkedExcelPath.Text ?? string.Empty).Trim();
            p.AutoSyncExcelOnRun = _cbAutoSyncExcel.IsChecked == true;

            p.WorksetMatchTokens = SplitCsv(_tbMatchTokens.Text);
            p.WorksetSuffixes = SplitCsv(_tbWorksetSuffixes.Text);
            p.TargetCategoryNames = _selectedCategories.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            p.InvalidateFilter();

            p.ParameterRules = _ruleRows
                .Where(r => !string.IsNullOrWhiteSpace(r.ParameterName) || !string.IsNullOrWhiteSpace(r.ValueOrFormula))
                .Select(r => r.ToRule())
                .ToList();

            p.TextNoteRules = _textNoteRows
                .Where(r => !string.IsNullOrWhiteSpace(r.FindPattern))
                .Select(r => r.ToRule())
                .ToList();

            if (double.TryParse(_tbSurveyN.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double sn)) p.SurveyNorthMm = sn;
            if (double.TryParse(_tbSurveyE.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double se)) p.SurveyEastMm = se;
            if (double.TryParse(_tbSurveyZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double sz)) p.SurveyElevMm = sz;

            p.SheetIncludeToken = (_tbSheetIncludeToken.Text ?? string.Empty).Trim();
            p.SheetDeletePatterns = SplitCsv(_tbSheetDeletePatterns.Text);
            p.SheetCoverName = (_tbSheetCoverName.Text ?? string.Empty).Trim();
            p.SyncWorkPackageCheckboxes = _cbSyncWpCheckboxes.IsChecked == true;

            p.ProjectInfoData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _projectInfoRows)
            {
                if (!string.IsNullOrWhiteSpace(row.Key))
                    p.ProjectInfoData[row.Key.Trim()] = row.Value ?? string.Empty;
            }

            p.DefaultFillParameters = _cbDefFillParams.IsChecked == true;
            p.DefaultAlignCoordinatesAndWorksets = _cbDefCoordWorksets.IsChecked == true;
            p.DefaultPurgeUnused = _cbDefPurge.IsChecked == true;
            p.DefaultExportNwc = _cbDefNwc.IsChecked == true;
            p.DefaultExportIfc = _cbDefIfc.IsChecked == true;
            p.DefaultSaveCentralRvt = _cbDefRvt.IsChecked == true;
            p.DefaultExportPdf = _cbDefPdf.IsChecked == true;
            p.DefaultExportDwg = _cbDefDwg.IsChecked == true;
            p.DefaultEnforceModelStandards = _cbDefStandards.IsChecked == true;

            if (!string.Equals(newCode, p.TradeCode, StringComparison.OrdinalIgnoreCase))
            {
                string oldCode = p.TradeCode;
                _workingProfiles.Remove(oldCode);
                p.TradeCode = newCode;
                _workingProfiles[newCode] = p;
                if (string.Equals(_activeTradeCode, oldCode, StringComparison.OrdinalIgnoreCase))
                    _activeTradeCode = newCode;
                _currentTradeCode = newCode;
                RefreshProfilesListBox();
            }
        }

        private void UpdateTemplatePreview()
        {
            string proj = string.IsNullOrWhiteSpace(_tbProjectNumber.Text) ? "P00100087" : _tbProjectNumber.Text.Trim();
            string orig = string.IsNullOrWhiteSpace(_tbOriginator.Text) ? "DAR" : _tbOriginator.Text.Trim();
            string code = string.IsNullOrWhiteSpace(_tbTradeCode.Text) ? "TI0" : _tbTradeCode.Text.Trim().ToUpperInvariant();
            string sampleLoc = "AFNXIEELS";

            string Format(string tpl) => (tpl ?? string.Empty)
                .Replace("{ProjectNumber}", proj)
                .Replace("{Originator}", orig)
                .Replace("{TradeCode}", code)
                .Replace("{Location}", sampleLoc);

            _txtTemplatePreview.Text =
                $"Preview ({sampleLoc}):\n" +
                $"  RVT: {Format(_tbRvtTemplate.Text)}.rvt\n" +
                $"  IFC: {Format(_tbIfcTemplate.Text)}.ifc\n" +
                $"  NWC: {Format(_tbNwcTemplate.Text)}.nwc";
        }

        // ── Excel Sync Handlers ──────────────────────────────────────────────

        private void OnBrowseLinkedExcel()
        {
            var dlg = new MicrosoftOpenFileDialog
            {
                Filter = "Excel Workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All Files (*.*)|*.*",
                Title = "Select Master Excel Workbook for Trade Profile"
            };
            if (dlg.ShowDialog() == true)
            {
                _tbLinkedExcelPath.Text = dlg.FileName;
                OnSyncNow();
            }
        }

        private void OnSyncNow()
        {
            SaveCurrentEditorToWorkingProfile();
            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var profile))
                return;

            string path = (_tbLinkedExcelPath.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                OnBrowseLinkedExcel();
                return;
            }

            var report = TradeExcelSyncService.SyncFromExcel(profile, path);
            if (!report.Success)
            {
                WpfMessageBox.Show(this, report.Message, "Excel Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadWorkingProfileToEditor(profile.TradeCode);
            RefreshProfilesListBox();

            string msg = $"Synced '{System.IO.Path.GetFileName(path)}':\n{report.Message}";
            if (report.Warnings.Count > 0)
                msg += "\n\nWarnings:\n• " + string.Join("\n• ", report.Warnings);

            _txtFooterStatus.Text = $"Synced {profile.TradeCode} from {System.IO.Path.GetFileName(path)} at {DateTime.Now:HH:mm:ss}";
            WpfMessageBox.Show(this, msg, "Excel Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnExportExcelTemplate()
        {
            SaveCurrentEditorToWorkingProfile();
            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var profile))
                return;

            var dlg = new MicrosoftSaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"{profile.TradeCode}_Master_Template.xlsx",
                Title = "Export Trade Profile Master Excel Template"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                TradeExcelSyncService.ExportToExcelTemplate(profile, dlg.FileName);
                _txtFooterStatus.Text = $"Exported template to {dlg.FileName}";
                var res = WpfMessageBox.Show(this,
                    $"Template exported to:\n{dlg.FileName}\n\nLink this Excel file to '{profile.TradeCode}' now?",
                    "Template Exported", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    _tbLinkedExcelPath.Text = dlg.FileName;
                    OnSyncNow();
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, $"Failed to export template:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnOpenLinkedExcel()
        {
            string path = (_tbLinkedExcelPath.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                WpfMessageBox.Show(this, "No existing Excel file is linked yet.", "Open Excel", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, $"Could not open file:\n{ex.Message}", "Open Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshExcelSyncTab(TradeProfile p)
        {
            if (string.IsNullOrWhiteSpace(p.LinkedExcelPath))
            {
                UiTheme.UpdateStatusPill(_syncStatusPill, "Not linked", StatusTone.Neutral);
                _txtSyncTimestamp.Text = "Link a workbook to enable auto-sync";
            }
            else if (!File.Exists(p.LinkedExcelPath))
            {
                UiTheme.UpdateStatusPill(_syncStatusPill, "File missing", StatusTone.Danger);
                _txtSyncTimestamp.Text = p.SyncedData != null ? "Using cached snapshot" : "File not found on disk";
            }
            else if (TradeExcelSyncService.IsExcelModifiedSinceSync(p))
            {
                UiTheme.UpdateStatusPill(_syncStatusPill, "Excel modified", StatusTone.Warning);
                _txtSyncTimestamp.Text = p.LastExcelSyncUtc.HasValue
                    ? $"Last synced {p.LastExcelSyncUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
                    : "Pending sync";
            }
            else
            {
                UiTheme.UpdateStatusPill(_syncStatusPill, "Synced", StatusTone.Success);
                _txtSyncTimestamp.Text = p.LastExcelSyncUtc.HasValue
                    ? $"Last synced {p.LastExcelSyncUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
                    : "";
            }

            var snap = p.SyncedData;
            _txtMetricElem.Text = (snap?.ElementParameters.Count ?? 0).ToString();
            _txtMetricSheet.Text = (snap?.SheetParameters.Count ?? 0).ToString();
            _txtMetricSpecific.Text = (snap?.SheetNumberParameters.Count ?? 0).ToString();
            _txtMetricStandards.Text = (snap?.StandardsFillRows.Count ?? 0).ToString();
            _txtMetricExports.Text = (snap?.ExportLocations.Count ?? 0).ToString();

            _excelColumnsPanel.Children.Clear();
            var cols = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snap != null)
            {
                foreach (var row in snap.ElementParameters.Values)
                    foreach (var k in row.Keys) cols.Add(k);
                foreach (var row in snap.SheetParameters.Values)
                    foreach (var k in row.Keys) cols.Add(k);
                foreach (var row in snap.SheetNumberParameters.Values)
                    foreach (var k in row.Keys) cols.Add(k);
            }

            if (cols.Count == 0)
            {
                _excelColumnsPanel.Children.Add(new TextBlock
                {
                    Text = "Sync an Excel workbook to inspect column headers and copy formula tokens.",
                    FontSize = 11.5,
                    Foreground = UiTheme.TextMutedBrush
                });
            }
            else
            {
                foreach (string col in cols)
                {
                    string token = $"[{col}]";
                    var chip = new WpfButton
                    {
                        Content = token,
                        Style = (Style)Resources["ChipButton"],
                        Margin = new Thickness(0, 0, 6, 6),
                        ToolTip = $"Click to copy {token} to clipboard"
                    };
                    chip.Click += (s, e) =>
                    {
                        try { WpfClipboard.SetText(token); } catch { }
                        _txtFooterStatus.Text = $"Copied {token} to clipboard";
                    };
                    _excelColumnsPanel.Children.Add(chip);
                }
            }
        }

        // ── Parameter Rules & Formula Preview ────────────────────────────────

        private void OnAddRule()
        {
            var vm = new ParameterRuleRowVm
            {
                Enabled = true,
                Target = ParameterRuleTarget.Element,
                Filter = "*",
                ParameterName = "New_Parameter",
                ValueOrFormula = "",
                Mode = ParameterFillMode.Overwrite
            };
            _ruleRows.Add(vm);
            _dgRules.SelectedItem = vm;
            RefreshFormulaPreviews();
        }

        private void OnDuplicateRule()
        {
            if (_dgRules.SelectedItem is not ParameterRuleRowVm sel) return;
            var copy = ParameterRuleRowVm.FromRule(sel.ToRule());
            copy.ParameterName = sel.ParameterName + "_Copy";
            int idx = _ruleRows.IndexOf(sel);
            _ruleRows.Insert(idx + 1, copy);
            _dgRules.SelectedItem = copy;
            RefreshFormulaPreviews();
        }

        private void OnRemoveRule()
        {
            if (_dgRules.SelectedItem is not ParameterRuleRowVm sel) return;
            _ruleRows.Remove(sel);
            RefreshFormulaPreviews();
        }

        private void MoveSelectedRule(int delta)
        {
            if (_dgRules.SelectedItem is not ParameterRuleRowVm sel) return;
            int idx = _ruleRows.IndexOf(sel);
            int targetIdx = idx + delta;
            if (targetIdx < 0 || targetIdx >= _ruleRows.Count) return;
            _ruleRows.Move(idx, targetIdx);
            _dgRules.SelectedItem = sel;
            RefreshFormulaPreviews();
        }

        private void PopulateSampleLocatorDropdown(TradeProfile p)
        {
            _cbSampleLocator.Items.Clear();
            _cbSampleLocator.Items.Add(new ComboBoxItem { Content = "Custom Sample Inputs", Tag = "__CUSTOM__" });

            var locators = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (p.SyncedData != null)
            {
                foreach (var k in p.SyncedData.ElementParameters.Keys) locators.Add(k);
                foreach (var k in p.SyncedData.SheetParameters.Keys) locators.Add(k);
            }

            foreach (string loc in locators)
                _cbSampleLocator.Items.Add(new ComboBoxItem { Content = $"Synced: {loc}", Tag = loc });

            _cbSampleLocator.SelectedIndex = locators.Count > 0 ? 1 : 0;
            LoadSampleInputsForSelectedLocator();
        }

        private void LoadSampleInputsForSelectedLocator()
        {
            if (_cbSampleLocator.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
                return;

            if (tag == "__CUSTOM__")
            {
                if (string.IsNullOrWhiteSpace(_tbSampleInputs.Text))
                    _tbSampleInputs.Text = "CPK_WBS_5=C.A.AI.NVL.10.CON.81; para1=shiv.mahto";
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var p))
                return;

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (p.SyncedData?.ElementParameters.TryGetValue(tag, out var el) == true)
                foreach (var kv in el) merged[kv.Key] = kv.Value;
            if (p.SyncedData?.SheetParameters.TryGetValue(tag, out var sh) == true)
                foreach (var kv in sh) if (!merged.ContainsKey(kv.Key)) merged[kv.Key] = kv.Value;

            _tbSampleInputs.Text = string.Join("; ", merged.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private void RefreshFormulaPreviews()
        {
            var sampleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string locSample = "AFNXIEELS";
            if (_cbSampleLocator.SelectedItem is ComboBoxItem ci && ci.Tag is string tag && tag != "__CUSTOM__")
                locSample = tag;

            string proj = string.IsNullOrWhiteSpace(_tbProjectNumber.Text) ? "P00100087" : _tbProjectNumber.Text.Trim();
            string orig = string.IsNullOrWhiteSpace(_tbOriginator.Text) ? "DAR" : _tbOriginator.Text.Trim();
            string code = string.IsNullOrWhiteSpace(_tbTradeCode.Text) ? "TI0" : _tbTradeCode.Text.Trim().ToUpperInvariant();
            string name = _tbTradeName.Text?.Trim() ?? "Trade";

            sampleMap["Location"] = locSample;
            sampleMap["Locator Code"] = locSample;
            sampleMap["LocatorCode"] = locSample;
            sampleMap["TradeCode"] = code;
            sampleMap["TradeName"] = name;
            sampleMap["ProjectNumber"] = proj;
            sampleMap["Originator"] = orig;
            sampleMap["RvtFileName"] = (_tbRvtTemplate.Text ?? "")
                .Replace("{ProjectNumber}", proj).Replace("{Originator}", orig)
                .Replace("{TradeCode}", code).Replace("{Location}", locSample);
            sampleMap["IfcFileName"] = (_tbIfcTemplate.Text ?? "")
                .Replace("{ProjectNumber}", proj).Replace("{Originator}", orig)
                .Replace("{TradeCode}", code).Replace("{Location}", locSample);
            sampleMap["NwcFileName"] = (_tbNwcTemplate.Text ?? "")
                .Replace("{ProjectNumber}", proj).Replace("{Originator}", orig)
                .Replace("{TradeCode}", code).Replace("{Location}", locSample);

            foreach (string pair in (_tbSampleInputs.Text ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string k = pair.Substring(0, eq).Trim();
                string v = pair.Substring(eq + 1).Trim();
                if (!string.IsNullOrEmpty(k)) sampleMap[k] = v;
            }

            var rules = _ruleRows.Select(r => r.ToRule()).ToList();
            var evaluated = ExcelFormulaEngine.EvaluateRulesOrdered(
                rules,
                k => sampleMap.TryGetValue(k, out var val) ? val : null,
                out var warnings);

            int errCount = 0;
            for (int i = 0; i < _ruleRows.Count && i < evaluated.Count; i++)
            {
                var vm = _ruleRows[i];
                var res = evaluated[i];
                if (!vm.Enabled)
                {
                    vm.SetPreview("(disabled)", false, true);
                }
                else if (res.Error != null)
                {
                    vm.SetPreview($"ERR: {res.Error}", true, false);
                    errCount++;
                }
                else
                {
                    vm.SetPreview(res.Value, false, false);
                }
            }

            if (errCount > 0)
            {
                _txtFormulaStatus.Text = $"{errCount} formula error(s) — hover Preview for details";
                _txtFormulaStatus.Foreground = UiTheme.DangerBrush;
            }
            else if (warnings.Count > 0)
            {
                _txtFormulaStatus.Text = warnings[0];
                _txtFormulaStatus.Foreground = UiTheme.WarningBrush;
            }
            else
            {
                _txtFormulaStatus.Text = $"{_ruleRows.Count(r => r.Enabled)} active rule(s) validated";
                _txtFormulaStatus.Foreground = UiTheme.SuccessBrush;
            }
        }

        private void ShowFormulaSyntaxDialog()
        {
            const string help =
                "PREFIX:\n" +
                "  Start any Value / Formula with '=' to evaluate as a formula.\n" +
                "  Without '=', the text is treated as a literal value.\n\n" +
                "REFERENCES:\n" +
                "  Wrap parameter names, Excel columns, or built-in tokens in brackets:\n" +
                "    [CPK_WBS_5], [para1], [Location], [TradeCode], [ProjectNumber],\n" +
                "    [Originator], [RvtFileName], [Sheet Number], [Level], [Category]\n\n" +
                "EXAMPLES:\n" +
                "  • Last 2 chars with dot (81 → 8.1):\n" +
                "      =LEFT(RIGHT([CPK_WBS_5],2),1)&\".\"&RIGHT([CPK_WBS_5],1)\n" +
                "  • Insert dot before last 2 chars:\n" +
                "      =LEFT([CPK_WBS_5],LEN([CPK_WBS_5])-2)&\".\"&RIGHT([CPK_WBS_5],2)\n" +
                "  • Extract first token before dot in uppercase:\n" +
                "      =UPPER(LEFT([para1],FIND(\".\",[para1])-1))\n" +
                "  • Conditional check:\n" +
                "      =IF(CONTAINS([Category],\"Tray\"),[Location]&\"_CT\",[Location])\n\n" +
                "SUPPORTED FUNCTIONS:\n" +
                "  Text:    LEFT, RIGHT, MID, LEN, FIND, SEARCH, SUBSTITUTE, REPLACE,\n" +
                "           UPPER, LOWER, PROPER, TRIM, CONCAT, CONCATENATE, REPT, EXACT, SPLIT\n" +
                "  Logic:   IF, IFERROR, ISBLANK, AND, OR, NOT, CONTAINS, STARTSWITH, ENDSWITH\n" +
                "  Numeric: VALUE, TEXT, ROUND, ABS, INT, MIN, MAX\n" +
                "  Ops:     &, +, -, *, /, =, <>, !=, <, >, <=, >=";

            WpfMessageBox.Show(this, help, "Excel Formula Syntax Reference", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Categories Tab Helpers ───────────────────────────────────────────

        private void RefreshCategoriesList()
        {
            _categoriesListStack.Children.Clear();
            string query = (_tbCategorySearch.Text ?? string.Empty).Trim();

            var known = RevitCategoryCatalog.AllCategories
                .Select(c => c.BuiltInCategoryName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var items = RevitCategoryCatalog.AllCategories
                .Select(c => (BuiltInName: c.BuiltInCategoryName, DisplayName: $"{c.DisplayName} ({c.Discipline})"))
                .ToList();

            foreach (string custom in _selectedCategories.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                if (!known.Contains(custom))
                    items.Add((custom, $"{custom} (Custom)"));
            }

            foreach (var (bic, display) in items)
            {
                if (!string.IsNullOrEmpty(query) &&
                    bic.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    display.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var cb = new WpfCheckBox
                {
                    Content = $"{display}  [{bic}]",
                    IsChecked = _selectedCategories.Contains(bic),
                    Style = (Style)Resources["ModernCheckBox"],
                    Margin = new Thickness(4, 3, 4, 3),
                    Tag = bic
                };
                cb.Checked += (s, e) => { _selectedCategories.Add(bic); UpdateCategoryCountBadge(); };
                cb.Unchecked += (s, e) => { _selectedCategories.Remove(bic); UpdateCategoryCountBadge(); };
                _categoriesListStack.Children.Add(cb);
            }

            UpdateCategoryCountBadge();
        }

        private void SetAllCategories(bool selected)
        {
            foreach (var child in _categoriesListStack.Children)
            {
                if (child is WpfCheckBox cb && cb.Tag is string bic)
                {
                    cb.IsChecked = selected;
                    if (selected) _selectedCategories.Add(bic);
                    else _selectedCategories.Remove(bic);
                }
            }
            UpdateCategoryCountBadge();
        }

        private void OnAddCustomCategory()
        {
            string raw = (_tbCustomCategory.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw)) return;
            _selectedCategories.Add(raw);
            _tbCustomCategory.Clear();
            RefreshCategoriesList();
        }

        private void UpdateCategoryCountBadge()
        {
            _txtCategoryCount.Text = $"{_selectedCategories.Count} selected";
        }

        // ── Profile Sidebar Actions ──────────────────────────────────────────

        private void OnNewProfile()
        {
            SaveCurrentEditorToWorkingProfile();
            string code = PromptForText("New Trade Profile", "Enter Trade Code (e.g. EL0, ME0, HV0):", "NEW1");
            if (string.IsNullOrWhiteSpace(code)) return;

            code = code.Trim().ToUpperInvariant();
            if (_workingProfiles.ContainsKey(code))
            {
                WpfMessageBox.Show(this, $"Profile '{code}' already exists.", "Duplicate Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var profile = new TradeProfile
            {
                TradeCode = code,
                TradeName = code,
                DisciplineFolder = code.Length >= 2 ? code.Substring(0, 2) : code,
                WorkPackageSubfolder = $"{code} - Building Package",
                WorksetMatchTokens = new List<string> { code },
                WorksetSuffixes = new List<string> { $"_{code}_01" },
                TargetCategoryNames = new List<string>()
            };
            profile.EnsureInitialized();

            _workingProfiles[code] = profile;
            _currentTradeCode = code;
            RefreshProfilesListBox();
            LoadWorkingProfileToEditor(code);
        }

        private void OnDuplicateProfile()
        {
            SaveCurrentEditorToWorkingProfile();
            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var src))
                return;

            string code = PromptForText("Duplicate Trade Profile", "Enter new Trade Code:", src.TradeCode + "_COPY");
            if (string.IsNullOrWhiteSpace(code)) return;

            code = code.Trim().ToUpperInvariant();
            if (_workingProfiles.ContainsKey(code))
            {
                WpfMessageBox.Show(this, $"Profile '{code}' already exists.", "Duplicate Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var copy = src.Clone();
            copy.TradeCode = code;
            copy.TradeName = src.TradeName + " (Copy)";
            _workingProfiles[code] = copy;
            _currentTradeCode = code;
            RefreshProfilesListBox();
            LoadWorkingProfileToEditor(code);
        }

        private void OnDeleteProfile()
        {
            if (_workingProfiles.Count <= 1)
            {
                WpfMessageBox.Show(this, "At least one trade profile must remain.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentTradeCode)) return;
            var res = WpfMessageBox.Show(this, $"Delete trade profile '{_currentTradeCode}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            _workingProfiles.Remove(_currentTradeCode!);
            if (string.Equals(_activeTradeCode, _currentTradeCode, StringComparison.OrdinalIgnoreCase))
                _activeTradeCode = _workingProfiles.Keys.First();
            _currentTradeCode = _workingProfiles.Keys.First();

            RefreshProfilesListBox();
            LoadWorkingProfileToEditor(_currentTradeCode);
        }

        private void OnResetBuiltins()
        {
            var res = WpfMessageBox.Show(this,
                "Restore built-in default trade profiles (TI0, EL0, ME0, PL0, FP0, AR0, ST0)?\nCustom profiles will be preserved.",
                "Reset Built-in Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            var temp = new TradeConfigManager();
            temp.ResetBuiltInTrades();
            foreach (var kv in temp.Trades)
            {
                var p = kv.Value.Clone();
                p.EnsureInitialized();
                _workingProfiles[kv.Key] = p;
            }

            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.ContainsKey(_currentTradeCode!))
                _currentTradeCode = _workingProfiles.Keys.First();

            RefreshProfilesListBox();
            LoadWorkingProfileToEditor(_currentTradeCode!);
        }

        private void OnImportJson()
        {
            var dlg = new MicrosoftOpenFileDialog { Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*", Title = "Import Trade Profiles JSON" };
            if (dlg.ShowDialog() != true) return;

            if (!TradeConfigManager.Instance.ImportFromJson(dlg.FileName, out string? err))
            {
                WpfMessageBox.Show(this, err ?? "Import failed.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _workingProfiles.Clear();
            foreach (var kv in TradeConfigManager.Instance.Trades)
            {
                var p = kv.Value.Clone();
                p.EnsureInitialized();
                _workingProfiles[kv.Key] = p;
            }
            _activeTradeCode = TradeConfigManager.Instance.ActiveTradeCode;
            _currentTradeCode = _activeTradeCode;
            RefreshProfilesListBox();
            LoadWorkingProfileToEditor(_currentTradeCode);
            _txtFooterStatus.Text = $"Imported profiles from {System.IO.Path.GetFileName(dlg.FileName)}";
        }

        private void OnExportJson()
        {
            SaveCurrentEditorToWorkingProfile();
            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var p))
                return;

            var dlg = new MicrosoftSaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = $"{p.TradeCode}_profile.json",
                Title = $"Export '{p.TradeCode}' Profile"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                TradeConfigManager.Instance.AddOrUpdateTrade(p.Clone());
                TradeConfigManager.Instance.ExportProfileToJson(p.TradeCode, dlg.FileName);
                _txtFooterStatus.Text = $"Exported {p.TradeCode} to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSetActive()
        {
            SaveCurrentEditorToWorkingProfile();
            if (string.IsNullOrWhiteSpace(_currentTradeCode)) return;
            _activeTradeCode = _currentTradeCode!;
            RefreshProfilesListBox();
            _txtFooterStatus.Text = $"Active trade set to {_activeTradeCode} (click Save & Apply to persist)";
        }

        private void OnSaveAndClose()
        {
            SaveCurrentEditorToWorkingProfile();

            var mgr = TradeConfigManager.Instance;
            mgr.Trades.Clear();
            foreach (var kvp in _workingProfiles)
                mgr.AddOrUpdateTrade(kvp.Value.Clone());

            mgr.ActiveTradeCode = _activeTradeCode;
            mgr.SaveToJson();

            DialogResult = true;
        }

        private string PromptForText(string title, string prompt, string defaultVal)
        {
            var win = new Window
            {
                Title = title,
                Width = 380,
                Height = 175,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };
            UiTheme.ApplyTheme(win);

            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8), Foreground = UiTheme.TextPrimaryBrush });
            var tb = new WpfTextBox { Text = defaultVal, Style = (Style)Resources["MonoTextBox"], Margin = new Thickness(0, 0, 0, 14) };
            sp.Children.Add(tb);

            var btns = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
            var ok = new WpfButton { Content = "OK", IsDefault = true, Style = (Style)Resources["PrimaryButton"], Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new WpfButton { Content = "Cancel", IsCancel = true, Style = (Style)Resources["SecondaryButton"], Padding = new Thickness(12, 5, 12, 5) };
            string result = string.Empty;
            ok.Click += (s, e) => { result = tb.Text; win.DialogResult = true; };
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            sp.Children.Add(btns);
            win.Content = sp;

            tb.SelectAll();
            tb.Focus();
            return win.ShowDialog() == true ? result : string.Empty;
        }

        private static List<string> SplitCsv(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToList();
        }

        // ── ViewModels ───────────────────────────────────────────────────────

        private sealed class ProfileListItem : Border
        {
            public string TradeCode { get; }

            public ProfileListItem(TradeProfile profile, bool isActive)
            {
                TradeCode = profile.TradeCode;
                Padding = new Thickness(8, 7, 8, 7);
                CornerRadius = new CornerRadius(4);

                var dock = new DockPanel();

                // Right sync status dot
                WpfColor dotColor = string.IsNullOrWhiteSpace(profile.LinkedExcelPath)
                    ? UiTheme.TextMutedColor
                    : !File.Exists(profile.LinkedExcelPath)
                        ? UiTheme.DangerColor
                        : TradeExcelSyncService.IsExcelModifiedSinceSync(profile)
                            ? UiTheme.WarningColor
                            : UiTheme.SuccessColor;

                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(dotColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = string.IsNullOrWhiteSpace(profile.LinkedExcelPath)
                        ? "No Excel linked"
                        : $"Excel: {System.IO.Path.GetFileName(profile.LinkedExcelPath)}"
                };
                DockPanel.SetDock(dot, Dock.Right);
                dock.Children.Add(dot);

                if (isActive)
                {
                    var badge = new Border
                    {
                        Background = UiTheme.AccentSoftBrush,
                        BorderBrush = UiTheme.AccentBorderBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = new TextBlock
                    {
                        Text = "ACTIVE",
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = UiTheme.AccentBrush
                    };
                    DockPanel.SetDock(badge, Dock.Right);
                    dock.Children.Add(badge);
                }

                var leftStack = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                leftStack.Children.Add(new TextBlock
                {
                    Text = profile.TradeCode,
                    FontFamily = UiTheme.MonoFont,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = UiTheme.TextPrimaryBrush,
                    Width = 44
                });
                leftStack.Children.Add(new TextBlock
                {
                    Text = profile.TradeName,
                    FontSize = 12,
                    Foreground = UiTheme.TextSecondaryBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                dock.Children.Add(leftStack);

                Child = dock;
            }
        }

        private sealed class ParameterRuleRowVm : INotifyPropertyChanged
        {
            private bool _enabled = true;
            private ParameterRuleTarget _target = ParameterRuleTarget.Element;
            private string _filter = "*";
            private string _parameterName = string.Empty;
            private string _valueOrFormula = string.Empty;
            private ParameterFillMode _mode = ParameterFillMode.Overwrite;
            private string _notes = string.Empty;
            private string _previewText = string.Empty;
            private SolidColorBrush _previewBrush = UiTheme.TextSecondaryBrush;

            public bool Enabled
            {
                get => _enabled;
                set { _enabled = value; OnPropertyChanged(nameof(Enabled)); }
            }

            public ParameterRuleTarget Target
            {
                get => _target;
                set { _target = value; OnPropertyChanged(nameof(Target)); }
            }

            public string Filter
            {
                get => _filter;
                set { _filter = value ?? "*"; OnPropertyChanged(nameof(Filter)); }
            }

            public string ParameterName
            {
                get => _parameterName;
                set { _parameterName = value ?? string.Empty; OnPropertyChanged(nameof(ParameterName)); }
            }

            public string ValueOrFormula
            {
                get => _valueOrFormula;
                set { _valueOrFormula = value ?? string.Empty; OnPropertyChanged(nameof(ValueOrFormula)); }
            }

            public ParameterFillMode Mode
            {
                get => _mode;
                set { _mode = value; OnPropertyChanged(nameof(Mode)); }
            }

            public string Notes
            {
                get => _notes;
                set { _notes = value ?? string.Empty; OnPropertyChanged(nameof(Notes)); }
            }

            public string PreviewText => _previewText;
            public SolidColorBrush PreviewBrush => _previewBrush;

            public void SetPreview(string text, bool isError, bool isDisabled)
            {
                _previewText = text;
                _previewBrush = isDisabled
                    ? UiTheme.TextMutedBrush
                    : isError
                        ? UiTheme.DangerBrush
                        : UiTheme.SuccessBrush;
                OnPropertyChanged(nameof(PreviewText));
                OnPropertyChanged(nameof(PreviewBrush));
            }

            public ParameterRule ToRule() => new()
            {
                Enabled = Enabled,
                Target = Target,
                Filter = string.IsNullOrWhiteSpace(Filter) ? "*" : Filter.Trim(),
                ParameterName = (ParameterName ?? string.Empty).Trim(),
                ValueOrFormula = ValueOrFormula ?? string.Empty,
                Mode = Mode,
                Notes = Notes ?? string.Empty
            };

            public static ParameterRuleRowVm FromRule(ParameterRule r) => new()
            {
                Enabled = r.Enabled,
                Target = r.Target,
                Filter = string.IsNullOrWhiteSpace(r.Filter) ? "*" : r.Filter,
                ParameterName = r.ParameterName,
                ValueOrFormula = r.ValueOrFormula,
                Mode = r.Mode,
                Notes = r.Notes
            };

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        private sealed class ProjectInfoEntryVm : INotifyPropertyChanged
        {
            private string _key = string.Empty;
            private string _value = string.Empty;

            public string Key
            {
                get => _key;
                set { _key = value ?? string.Empty; OnPropertyChanged(nameof(Key)); }
            }

            public string Value
            {
                get => _value;
                set { _value = value ?? string.Empty; OnPropertyChanged(nameof(Value)); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        // ── Sheet TextNote Rule Handlers & ViewModel ─────────────────────────

        private void OnAddTextNoteRule()
        {
            var vm = new TextNoteRuleRowVm
            {
                Enabled = true,
                SheetFilter = "*TIU*",
                Scope = TextNoteScope.SheetsPlacedViewsAndLegends,
                NoteContains = "",
                FindPattern = "P00100087-DAR-MIF-[A-Z0-9]+",
                IsRegex = true,
                ReplaceWith = @"=[ProjectNumber]&""-""&[Originator]&""-MIF-""&[Location]"
            };
            _textNoteRows.Add(vm);
            _dgTextNoteRules.SelectedItem = vm;
            RefreshTextNotePreview();
        }

        private void OnDuplicateTextNoteRule()
        {
            if (_dgTextNoteRules.SelectedItem is not TextNoteRuleRowVm sel) return;
            var copy = TextNoteRuleRowVm.FromRule(sel.ToRule());
            int idx = _textNoteRows.IndexOf(sel);
            _textNoteRows.Insert(idx + 1, copy);
            _dgTextNoteRules.SelectedItem = copy;
            RefreshTextNotePreview();
        }

        private void OnRemoveTextNoteRule()
        {
            if (_dgTextNoteRules.SelectedItem is not TextNoteRuleRowVm sel) return;
            _textNoteRows.Remove(sel);
            RefreshTextNotePreview();
        }

        private void OnAddTiuPresetRule()
        {
            var vm = TextNoteRuleRowVm.FromRule(TextNoteRule.CreateDefaultTiuInertGasRule());
            _textNoteRows.Add(vm);
            _dgTextNoteRules.SelectedItem = vm;
            RefreshTextNotePreview();
        }

        private void RefreshTextNotePreview()
        {
            string input = _tbTnSampleInput.Text ?? string.Empty;
            string sheetNum = (_tbTnSampleSheet.Text ?? string.Empty).Trim();
            string loc = string.IsNullOrWhiteSpace(_tbTnSampleLocator.Text) ? "AFNXIEELS" : _tbTnSampleLocator.Text.Trim();
            string proj = string.IsNullOrWhiteSpace(_tbProjectNumber.Text) ? "P00100087" : _tbProjectNumber.Text.Trim();
            string orig = string.IsNullOrWhiteSpace(_tbOriginator.Text) ? "DAR" : _tbOriginator.Text.Trim();
            string code = string.IsNullOrWhiteSpace(_tbTradeCode.Text) ? "TI0" : _tbTradeCode.Text.Trim().ToUpperInvariant();

            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Location", loc },
                { "Locator Code", loc },
                { "LocatorCode", loc },
                { "ProjectNumber", proj },
                { "Originator", orig },
                { "TradeCode", code },
                { "Sheet Number", sheetNum },
                { "SheetNumber", sheetNum }
            };

            string working = input;
            bool changed = false;
            string? firstError = null;

            foreach (var rowVm in _textNoteRows)
            {
                var rule = rowVm.ToRule();
                if (!rule.Enabled) continue;
                if (!TextNoteRuleProcessor.MatchesSheetFilter(sheetNum, sheetNum, rule.SheetFilter)) continue;

                if (TextNoteRuleProcessor.ApplyRuleToText(rule, working, k => lookup.TryGetValue(k, out var v) ? v : null, out string next, out string? err))
                {
                    working = next;
                    changed = true;
                }
                else if (!string.IsNullOrEmpty(err) && firstError == null)
                {
                    firstError = err;
                }
            }

            _tbTnSampleOutput.Text = working;
            if (!string.IsNullOrEmpty(firstError))
            {
                _txtTnStatus.Text = $"Error: {firstError}";
                _txtTnStatus.Foreground = UiTheme.DangerBrush;
            }
            else if (changed)
            {
                _txtTnStatus.Text = "Matched & updated";
                _txtTnStatus.Foreground = UiTheme.SuccessBrush;
            }
            else
            {
                _txtTnStatus.Text = "No rule matched — unchanged";
                _txtTnStatus.Foreground = UiTheme.TextMutedBrush;
            }
        }

        private sealed class TextNoteRuleRowVm : INotifyPropertyChanged
        {
            private bool _enabled = true;
            private string _sheetFilter = "*";
            private TextNoteScope _scope = TextNoteScope.SheetsPlacedViewsAndLegends;
            private string _noteContains = string.Empty;
            private string _findPattern = string.Empty;
            private bool _isRegex = true;
            private string _replaceWith = string.Empty;
            private string _notes = string.Empty;

            public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(nameof(Enabled)); } }
            public string SheetFilter { get => _sheetFilter; set { _sheetFilter = value ?? "*"; OnPropertyChanged(nameof(SheetFilter)); } }
            public TextNoteScope Scope { get => _scope; set { _scope = value; OnPropertyChanged(nameof(Scope)); } }
            public string NoteContains { get => _noteContains; set { _noteContains = value ?? string.Empty; OnPropertyChanged(nameof(NoteContains)); } }
            public string FindPattern { get => _findPattern; set { _findPattern = value ?? string.Empty; OnPropertyChanged(nameof(FindPattern)); } }
            public bool IsRegex { get => _isRegex; set { _isRegex = value; OnPropertyChanged(nameof(IsRegex)); } }
            public string ReplaceWith { get => _replaceWith; set { _replaceWith = value ?? string.Empty; OnPropertyChanged(nameof(ReplaceWith)); } }
            public string Notes { get => _notes; set { _notes = value ?? string.Empty; OnPropertyChanged(nameof(Notes)); } }

            public TextNoteRule ToRule() => new()
            {
                Enabled = Enabled,
                SheetFilter = string.IsNullOrWhiteSpace(SheetFilter) ? "*" : SheetFilter.Trim(),
                Scope = Scope,
                NoteContains = NoteContains ?? string.Empty,
                FindPattern = FindPattern ?? string.Empty,
                IsRegex = IsRegex,
                ReplaceWith = ReplaceWith ?? string.Empty,
                Notes = Notes ?? string.Empty
            };

            public static TextNoteRuleRowVm FromRule(TextNoteRule r) => new()
            {
                Enabled = r.Enabled,
                SheetFilter = string.IsNullOrWhiteSpace(r.SheetFilter) ? "*" : r.SheetFilter,
                Scope = r.Scope,
                NoteContains = r.NoteContains,
                FindPattern = r.FindPattern,
                IsRegex = r.IsRegex,
                ReplaceWith = r.ReplaceWith,
                Notes = r.Notes
            };

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}
