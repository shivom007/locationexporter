using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MicrosoftOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MicrosoftSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using RevitLocationExporter.Configuration;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;

namespace RevitLocationExporter.Form
{
    /// <summary>
    /// Interactive dialog that allows users to create, customize, duplicate, delete,
    /// import, and export TradeProfiles and persist them to JSON.
    /// </summary>
    public sealed class TradeProfileManagerDialog : Window
    {
        private readonly Dictionary<string, TradeProfile> _workingProfiles =
            new(StringComparer.OrdinalIgnoreCase);

        private string? _currentTradeCode;
        private string _activeTradeCode;

        // UI Controls - Left Panel
        private readonly WpfListBox _lbProfiles;
        private readonly TextBlock _txtStatus;

        // UI Controls - Editor Fields
        private readonly WpfTextBox _tbTradeCode;
        private readonly WpfTextBox _tbTradeName;
        private readonly WpfTextBox _tbDisciplineFolder;
        private readonly WpfTextBox _tbWorkPackageSubfolder;

        private readonly WpfTextBox _tbMatchTokens;
        private readonly WpfTextBox _tbWorksetSuffixes;

        private readonly WpfTextBox _tbRvtTemplate;
        private readonly WpfTextBox _tbIfcTemplate;
        private readonly WpfTextBox _tbNwcTemplate;

        // Categories UI
        private readonly WpfTextBox _tbCategorySearch;
        private readonly StackPanel _categoriesListStack;
        private readonly WpfTextBox _tbCustomCategory;
        private readonly HashSet<string> _selectedCategories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WpfCheckBox> _categoryCheckboxes = new(StringComparer.OrdinalIgnoreCase);

        private bool _isUpdatingEditor = false;

        public TradeProfileManagerDialog(string? initialTradeCode = null)
        {
            Title = "Trade Profile Manager & Builder";
            Width = 1000;
            Height = 720;
            MinWidth = 860;
            MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(WpfColor.FromRgb(245, 245, 248));

            var configMgr = TradeConfigManager.Instance;
            _activeTradeCode = configMgr.ActiveTradeCode ?? "TI0";

            // Clone profiles for editing in memory
            foreach (var kvp in configMgr.Trades)
            {
                _workingProfiles[kvp.Key] = kvp.Value.Clone();
            }

            if (_workingProfiles.Count == 0)
            {
                var def = new TradeProfile();
                _workingProfiles[def.TradeCode] = def;
            }

            if (!string.IsNullOrWhiteSpace(initialTradeCode) && _workingProfiles.ContainsKey(initialTradeCode!))
            {
                _currentTradeCode = initialTradeCode;
            }
            else if (!string.IsNullOrWhiteSpace(_activeTradeCode) && _workingProfiles.ContainsKey(_activeTradeCode))
            {
                _currentTradeCode = _activeTradeCode;
            }
            else
            {
                _currentTradeCode = _workingProfiles.Keys.FirstOrDefault() ?? "TI0";
            }

            // ── Root Layout ──────────────────────────────────────────────────
            var root = new DockPanel();
            Content = root;

            // ── Bottom Action Bar ────────────────────────────────────────────
            var bottomBar = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(235, 235, 240)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(200, 200, 210)),
                Padding = new Thickness(14, 10, 14, 10)
            };
            DockPanel.SetDock(bottomBar, Dock.Bottom);

            var bottomDock = new DockPanel();
            bottomBar.Child = bottomDock;
            root.Children.Add(bottomBar);

            _txtStatus = new TextBlock
            {
                Text = $"Config Path: {TradeConfigManager.GetConfigFilePath()}",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 105, 115))
            };
            DockPanel.SetDock(_txtStatus, Dock.Left);
            bottomDock.Children.Add(_txtStatus);

            var bottomBtnStack = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            DockPanel.SetDock(bottomBtnStack, Dock.Right);

            var btnSetActive = new WpfButton
            {
                Content = "Set as Active Trade",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(WpfColor.FromRgb(40, 140, 70)),
                Foreground = WpfBrushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                ToolTip = "Sets the currently selected profile as the default active trade."
            };
            btnSetActive.Click += (s, e) => OnSetActive();

            var btnSaveClose = new WpfButton
            {
                Content = "Save & Apply",
                IsDefault = true,
                Padding = new Thickness(18, 6, 18, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(WpfColor.FromRgb(0, 120, 215)),
                Foreground = WpfBrushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0)
            };
            btnSaveClose.Click += (s, e) => OnSaveAndClose();

            var btnCancel = new WpfButton
            {
                Content = "Cancel",
                IsCancel = true,
                Padding = new Thickness(16, 6, 16, 6)
            };
            btnCancel.Click += (s, e) => { DialogResult = false; };

            bottomBtnStack.Children.Add(btnSetActive);
            bottomBtnStack.Children.Add(btnSaveClose);
            bottomBtnStack.Children.Add(btnCancel);
            bottomDock.Children.Add(bottomBtnStack);

            // ── Main Two-Column Grid ─────────────────────────────────────────
            var mainGrid = new Grid { Margin = new Thickness(12) };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); // Spacer
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(mainGrid);

            // ════════════════════════════════════════════════════════════════
            // LEFT PANEL: PROFILES LIST & ACTIONS
            // ════════════════════════════════════════════════════════════════
            var leftCard = new Border
            {
                Background = WpfBrushes.White,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(215, 220, 230)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10)
            };
            Grid.SetColumn(leftCard, 0);
            mainGrid.Children.Add(leftCard);

            var leftDock = new DockPanel();
            leftCard.Child = leftDock;

            // Header
            var leftHeader = new TextBlock
            {
                Text = "Trade Profiles",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(20, 60, 110)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(leftHeader, Dock.Top);
            leftDock.Children.Add(leftHeader);

            // Left Toolbar (New, Duplicate, Delete)
            var profileActionPanel = new StackPanel
            {
                Orientation = WpfOrientation.Vertical,
                Margin = new Thickness(0, 8, 0, 0)
            };
            DockPanel.SetDock(profileActionPanel, Dock.Bottom);

            var row1 = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnNew = new WpfButton { Content = "+ New", Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(4) };
            btnNew.Click += (s, e) => OnNewProfile();
            Grid.SetColumn(btnNew, 0);
            row1.Children.Add(btnNew);

            var btnDuplicate = new WpfButton { Content = "Duplicate", Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(4) };
            btnDuplicate.Click += (s, e) => OnDuplicateProfile();
            Grid.SetColumn(btnDuplicate, 1);
            row1.Children.Add(btnDuplicate);
            profileActionPanel.Children.Add(row1);

            var row2 = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnDelete = new WpfButton { Content = "Delete", Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(4) };
            btnDelete.Click += (s, e) => OnDeleteProfile();
            Grid.SetColumn(btnDelete, 0);
            row2.Children.Add(btnDelete);

            var btnResetBuiltins = new WpfButton { Content = "Reload Built-in", Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(4) };
            btnResetBuiltins.Click += (s, e) => OnResetBuiltins();
            Grid.SetColumn(btnResetBuiltins, 1);
            row2.Children.Add(btnResetBuiltins);
            profileActionPanel.Children.Add(row2);

            var row3 = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnImport = new WpfButton { Content = "Import JSON...", Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(4) };
            btnImport.Click += (s, e) => OnImportJson();
            Grid.SetColumn(btnImport, 0);
            row3.Children.Add(btnImport);

            var btnExport = new WpfButton { Content = "Export JSON...", Margin = new Thickness(3, 0, 0, 0), Padding = new Thickness(4) };
            btnExport.Click += (s, e) => OnExportJson();
            Grid.SetColumn(btnExport, 1);
            row3.Children.Add(btnExport);
            profileActionPanel.Children.Add(row3);

            leftDock.Children.Add(profileActionPanel);

            // ListBox
            _lbProfiles = new WpfListBox
            {
                FontSize = 12,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(220, 225, 235))
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

            // ════════════════════════════════════════════════════════════════
            // RIGHT PANEL: PROFILE EDITOR
            // ════════════════════════════════════════════════════════════════
            var rightCard = new Border
            {
                Background = WpfBrushes.White,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(215, 220, 230)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 12, 14, 12)
            };
            Grid.SetColumn(rightCard, 2);
            mainGrid.Children.Add(rightCard);

            var rightScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            rightCard.Child = rightScroll;

            var editorStack = new StackPanel();
            rightScroll.Content = editorStack;

            // Section 1: Trade Identity
            var sec1Title = CreateSectionHeader("1. Trade Identity & Folder Structure");
            editorStack.Children.Add(sec1Title);

            var idGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            idGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            idGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            idGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            idGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            idGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddFormRow(idGrid, 0, "Trade Code:", _tbTradeCode = new WpfTextBox { ToolTip = "Short code matching ISO 19650 filename segment 2 (e.g. TI0, EL0, ME0, PL0, AR0)" });
            AddFormRow(idGrid, 1, "Trade Name:", _tbTradeName = new WpfTextBox { ToolTip = "Friendly discipline name (e.g. Telecom, Mechanical HVAC, Electrical)" });
            AddFormRow(idGrid, 2, "Discipline Folder:", _tbDisciplineFolder = new WpfTextBox { ToolTip = "Discipline code used in folder paths (e.g. EL, ME, PL, AR, ST)" });
            AddFormRow(idGrid, 3, "Work Package Folder:", _tbWorkPackageSubfolder = new WpfTextBox { ToolTip = "Folder name under Models (e.g. Telecom - Building Package, Mechanical - Building Package)" });
            editorStack.Children.Add(idGrid);

            // Section 2: Worksets Rules
            var sec2Title = CreateSectionHeader("2. Workset Rules & Renaming");
            editorStack.Children.Add(sec2Title);

            var wsGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            wsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            wsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            wsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddFormRow(wsGrid, 0, "Match Tokens:", _tbMatchTokens = new WpfTextBox { ToolTip = "Comma-separated tokens to identify worksets to open (e.g. TI0, TELECOM)" });
            AddFormRow(wsGrid, 1, "Suffixes to Rename:", _tbWorksetSuffixes = new WpfTextBox { ToolTip = "Comma-separated suffixes to rename per location (e.g. _TI0_FA, _TI0_SD, _TI0_DD)" });
            editorStack.Children.Add(wsGrid);

            // Section 3: File Naming Templates
            var sec3Title = CreateSectionHeader("3. Output Filename Templates");
            editorStack.Children.Add(sec3Title);

            var hintText = new TextBlock
            {
                Text = "Available tokens: {ProjectNumber}, {Originator}, {TradeCode}, {Location}",
                FontSize = 11,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(110, 115, 125)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            editorStack.Children.Add(hintText);

            var fnGrid = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            fnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            fnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fnGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fnGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fnGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddFormRow(fnGrid, 0, "Central RVT:", _tbRvtTemplate = new WpfTextBox());
            AddFormRow(fnGrid, 1, "Export IFC:", _tbIfcTemplate = new WpfTextBox());
            AddFormRow(fnGrid, 2, "Export NWC:", _tbNwcTemplate = new WpfTextBox());
            editorStack.Children.Add(fnGrid);

            // Section 4: Target Revit Categories
            var sec4Title = CreateSectionHeader("4. Target Revit Categories (for CPK Parameters & Processing)");
            editorStack.Children.Add(sec4Title);

            // Categories toolbar & search
            var catToolbar = new DockPanel { Margin = new Thickness(0, 4, 0, 6) };

            var catPresetStack = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            DockPanel.SetDock(catPresetStack, Dock.Right);

            var btnSelectAllCat = MakeSmallButton("Select All");
            btnSelectAllCat.Click += (s, e) => SetAllCategories(true);
            var btnClearAllCat = MakeSmallButton("Clear All");
            btnClearAllCat.Click += (s, e) => SetAllCategories(false);

            catPresetStack.Children.Add(btnSelectAllCat);
            catPresetStack.Children.Add(btnClearAllCat);
            catToolbar.Children.Add(catPresetStack);

            _tbCategorySearch = new WpfTextBox
            {
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(4, 2, 4, 2),
                ToolTip = "Type to search Revit categories..."
            };
            _tbCategorySearch.TextChanged += (s, e) => RefreshCategoriesList();
            catToolbar.Children.Add(_tbCategorySearch);

            editorStack.Children.Add(catToolbar);

            // Scrollable categories box
            var catScrollBorder = new Border
            {
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(220, 225, 235)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(WpfColor.FromRgb(250, 251, 253)),
                Height = 180,
                CornerRadius = new CornerRadius(3)
            };
            var catScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _categoriesListStack = new StackPanel { Margin = new Thickness(6) };
            catScroll.Content = _categoriesListStack;
            catScrollBorder.Child = catScroll;
            editorStack.Children.Add(catScrollBorder);

            // Custom category input row
            var customCatRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var btnAddCustom = new WpfButton
            {
                Content = "Add Custom Category",
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(6, 0, 0, 0)
            };
            btnAddCustom.Click += (s, e) => OnAddCustomCategory();
            DockPanel.SetDock(btnAddCustom, Dock.Right);
            customCatRow.Children.Add(btnAddCustom);

            _tbCustomCategory = new WpfTextBox
            {
                ToolTip = "Enter BuiltInCategory enum name (e.g. OST_CableTray, OST_Walls)"
            };
            customCatRow.Children.Add(_tbCustomCategory);
            editorStack.Children.Add(customCatRow);

            // Populate profiles and load initial
            RefreshProfileList();
            if (!string.IsNullOrWhiteSpace(_currentTradeCode))
                LoadWorkingProfileToEditor(_currentTradeCode!);
        }

        // ════════════════════════════════════════════════════════════════
        // PROFILE LIST MANAGEMENT
        // ════════════════════════════════════════════════════════════════

        private class ProfileListItem
        {
            public string TradeCode { get; set; } = string.Empty;
            public string DisplayText { get; set; } = string.Empty;
            public override string ToString() => DisplayText;
        }

        private void RefreshProfileList()
        {
            _isUpdatingEditor = true;
            _lbProfiles.Items.Clear();

            foreach (var kvp in _workingProfiles.OrderBy(k => k.Key))
            {
                string code = kvp.Key;
                var p = kvp.Value;
                bool isActive = string.Equals(code, _activeTradeCode, StringComparison.OrdinalIgnoreCase);

                string badge = isActive ? " [Active]" : "";
                var item = new ProfileListItem
                {
                    TradeCode = code,
                    DisplayText = $"[{code}] {p.TradeName}{badge}"
                };

                _lbProfiles.Items.Add(item);

                if (string.Equals(code, _currentTradeCode, StringComparison.OrdinalIgnoreCase))
                {
                    _lbProfiles.SelectedItem = item;
                }
            }

            if (_lbProfiles.SelectedItem == null && _lbProfiles.Items.Count > 0)
            {
                _lbProfiles.SelectedIndex = 0;
                if (_lbProfiles.SelectedItem is ProfileListItem first)
                    _currentTradeCode = first.TradeCode;
            }

            _isUpdatingEditor = false;
        }

        private void LoadWorkingProfileToEditor(string tradeCode)
        {
            if (string.IsNullOrWhiteSpace(tradeCode) || !_workingProfiles.TryGetValue(tradeCode, out var profile))
                return;

            _isUpdatingEditor = true;

            _tbTradeCode.Text = profile.TradeCode;
            _tbTradeName.Text = profile.TradeName;
            _tbDisciplineFolder.Text = profile.DisciplineFolder;
            _tbWorkPackageSubfolder.Text = profile.WorkPackageSubfolder;

            _tbMatchTokens.Text = string.Join(", ", profile.WorksetMatchTokens ?? new List<string>());
            _tbWorksetSuffixes.Text = string.Join(", ", profile.WorksetSuffixes ?? new List<string>());

            _tbRvtTemplate.Text = profile.RvtFileNameTemplate;
            _tbIfcTemplate.Text = profile.IfcFileNameTemplate;
            _tbNwcTemplate.Text = profile.NwcFileNameTemplate;

            _selectedCategories.Clear();
            if (profile.TargetCategoryNames != null)
            {
                foreach (string cat in profile.TargetCategoryNames)
                    _selectedCategories.Add(cat);
            }

            RefreshCategoriesList();

            _isUpdatingEditor = false;
        }

        private void SaveCurrentEditorToWorkingProfile()
        {
            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var profile))
                return;

            string newCode = _tbTradeCode.Text.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(newCode))
            {
                // If code was renamed
                if (!string.Equals(newCode, _currentTradeCode, StringComparison.OrdinalIgnoreCase))
                {
                    _workingProfiles.Remove(_currentTradeCode!);
                    profile.TradeCode = newCode;
                    _workingProfiles[newCode] = profile;

                    if (string.Equals(_activeTradeCode, _currentTradeCode, StringComparison.OrdinalIgnoreCase))
                        _activeTradeCode = newCode;

                    _currentTradeCode = newCode;
                }
            }

            profile.TradeName = _tbTradeName.Text.Trim();
            profile.DisciplineFolder = _tbDisciplineFolder.Text.Trim();
            profile.WorkPackageSubfolder = _tbWorkPackageSubfolder.Text.Trim();

            profile.WorksetMatchTokens = _tbMatchTokens.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.WorksetSuffixes = _tbWorksetSuffixes.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.RvtFileNameTemplate = _tbRvtTemplate.Text.Trim();
            profile.IfcFileNameTemplate = _tbIfcTemplate.Text.Trim();
            profile.NwcFileNameTemplate = _tbNwcTemplate.Text.Trim();

            profile.TargetCategoryNames = _selectedCategories.OrderBy(c => c).ToList();
            profile.InvalidateFilter();
        }

        // ════════════════════════════════════════════════════════════════
        // CATEGORIES LIST MANAGEMENT
        // ════════════════════════════════════════════════════════════════

        private void RefreshCategoriesList()
        {
            _categoriesListStack.Children.Clear();
            _categoryCheckboxes.Clear();

            string search = _tbCategorySearch.Text.Trim();

            // 1. Known catalog categories grouped by discipline
            var groups = RevitCategoryCatalog.AllCategories
                .GroupBy(c => c.Discipline);

            foreach (var group in groups)
            {
                var matchingItems = group.Where(item =>
                    string.IsNullOrEmpty(search) ||
                    item.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.BuiltInCategoryName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                if (matchingItems.Count == 0) continue;

                var groupHeader = new TextBlock
                {
                    Text = group.Key,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(40, 70, 120)),
                    Margin = new Thickness(0, 6, 0, 2)
                };
                _categoriesListStack.Children.Add(groupHeader);

                var wrap = new WrapPanel { Orientation = WpfOrientation.Horizontal };
                foreach (var cat in matchingItems)
                {
                    bool isChecked = _selectedCategories.Contains(cat.BuiltInCategoryName);
                    var cb = new WpfCheckBox
                    {
                        Content = $"{cat.DisplayName} ({cat.BuiltInCategoryName})",
                        IsChecked = isChecked,
                        Margin = new Thickness(0, 2, 12, 2),
                        MinWidth = 240,
                        FontSize = 11
                    };

                    string catName = cat.BuiltInCategoryName;
                    cb.Checked += (s, e) => _selectedCategories.Add(catName);
                    cb.Unchecked += (s, e) => _selectedCategories.Remove(catName);

                    _categoryCheckboxes[catName] = cb;
                    wrap.Children.Add(cb);
                }
                _categoriesListStack.Children.Add(wrap);
            }

            // 2. Extra custom categories in _selectedCategories not in catalog
            var catalogNames = new HashSet<string>(
                RevitCategoryCatalog.AllCategories.Select(c => c.BuiltInCategoryName),
                StringComparer.OrdinalIgnoreCase);

            var customSelected = _selectedCategories
                .Where(c => !catalogNames.Contains(c))
                .Where(c => string.IsNullOrEmpty(search) || c.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (customSelected.Count > 0)
            {
                var customHeader = new TextBlock
                {
                    Text = "Custom Added Categories",
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(140, 40, 40)),
                    Margin = new Thickness(0, 8, 0, 2)
                };
                _categoriesListStack.Children.Add(customHeader);

                var customWrap = new WrapPanel { Orientation = WpfOrientation.Horizontal };
                foreach (string catName in customSelected)
                {
                    var cb = new WpfCheckBox
                    {
                        Content = catName,
                        IsChecked = true,
                        Margin = new Thickness(0, 2, 12, 2),
                        MinWidth = 240,
                        FontSize = 11
                    };
                    cb.Checked += (s, e) => _selectedCategories.Add(catName);
                    cb.Unchecked += (s, e) => _selectedCategories.Remove(catName);

                    _categoryCheckboxes[catName] = cb;
                    customWrap.Children.Add(cb);
                }
                _categoriesListStack.Children.Add(customWrap);
            }
        }

        private void SetAllCategories(bool checkAll)
        {
            if (checkAll)
            {
                foreach (var cat in RevitCategoryCatalog.AllCategories)
                    _selectedCategories.Add(cat.BuiltInCategoryName);
            }
            else
            {
                _selectedCategories.Clear();
            }

            RefreshCategoriesList();
        }

        private void OnAddCustomCategory()
        {
            string cat = _tbCustomCategory.Text.Trim();
            if (string.IsNullOrWhiteSpace(cat)) return;

            if (!cat.StartsWith("OST_", StringComparison.OrdinalIgnoreCase))
                cat = "OST_" + cat;

            if (!RevitCategoryCatalog.IsValidBuiltInCategory(cat))
            {
                var res = WpfMessageBox.Show(
                    $"'{cat}' is not recognized as a standard Revit BuiltInCategory enum.\nDo you still want to add it?",
                    "Custom Category Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res != MessageBoxResult.Yes) return;
            }

            _selectedCategories.Add(cat);
            _tbCustomCategory.Text = string.Empty;
            RefreshCategoriesList();
        }

        // ════════════════════════════════════════════════════════════════
        // BUTTON ACTIONS
        // ════════════════════════════════════════════════════════════════

        private void OnNewProfile()
        {
            SaveCurrentEditorToWorkingProfile();

            string newCode = "CUSTOM" + (_workingProfiles.Count + 1);
            int counter = 1;
            while (_workingProfiles.ContainsKey(newCode))
            {
                counter++;
                newCode = $"CUSTOM{counter}";
            }

            var newProfile = new TradeProfile
            {
                TradeCode = newCode,
                TradeName = "New Trade",
                DisciplineFolder = "XX",
                WorkPackageSubfolder = "New Trade - Building Package",
                WorksetMatchTokens = new() { newCode },
                WorksetSuffixes = new() { $"_{newCode}_01" },
                TargetCategoryNames = new() { "OST_GenericModel" }
            };

            _workingProfiles[newCode] = newProfile;
            _currentTradeCode = newCode;
            RefreshProfileList();
            LoadWorkingProfileToEditor(newCode);
        }

        private void OnDuplicateProfile()
        {
            SaveCurrentEditorToWorkingProfile();

            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var current))
                return;

            string cloneCode = current.TradeCode + "_COPY";
            int counter = 1;
            while (_workingProfiles.ContainsKey(cloneCode))
            {
                counter++;
                cloneCode = $"{current.TradeCode}_COPY{counter}";
            }

            var clone = current.Clone();
            clone.TradeCode = cloneCode;
            clone.TradeName = $"{current.TradeName} (Copy)";

            _workingProfiles[cloneCode] = clone;
            _currentTradeCode = cloneCode;
            RefreshProfileList();
            LoadWorkingProfileToEditor(cloneCode);
        }

        private void OnDeleteProfile()
        {
            if (_workingProfiles.Count <= 1)
            {
                WpfMessageBox.Show("Cannot delete the last remaining trade profile.", "Delete Profile",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentTradeCode)) return;

            var result = WpfMessageBox.Show(
                $"Are you sure you want to delete profile '{_currentTradeCode}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _workingProfiles.Remove(_currentTradeCode!);

            if (string.Equals(_activeTradeCode, _currentTradeCode, StringComparison.OrdinalIgnoreCase))
                _activeTradeCode = _workingProfiles.Keys.FirstOrDefault() ?? "TI0";

            _currentTradeCode = _workingProfiles.Keys.FirstOrDefault() ?? "TI0";
            RefreshProfileList();
            LoadWorkingProfileToEditor(_currentTradeCode);
        }

        private void OnResetBuiltins()
        {
            var result = WpfMessageBox.Show(
                "Do you want to reload standard built-in profiles (TI0, EL0, ME0, PL0, FP0, AR0, ST0)?\nCustom profiles with unique codes will be preserved.",
                "Reload Built-in Profiles",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Save editor first
            SaveCurrentEditorToWorkingProfile();

            // Re-register builtins
            var tempMgr = new TradeConfigManager();
            foreach (var kvp in tempMgr.Trades)
            {
                _workingProfiles[kvp.Key] = kvp.Value.Clone();
            }

            RefreshProfileList();
            if (!string.IsNullOrWhiteSpace(_currentTradeCode) && _workingProfiles.ContainsKey(_currentTradeCode!))
                LoadWorkingProfileToEditor(_currentTradeCode!);
        }

        private void OnImportJson()
        {
            var ofd = new MicrosoftOpenFileDialog
            {
                Title = "Import Trade Profile(s) from JSON",
                Filter = "JSON Configuration (*.json)|*.json|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true) return;

            bool success = TradeConfigManager.Instance.ImportFromJson(ofd.FileName, out string? error);
            if (!success)
            {
                WpfMessageBox.Show($"Import failed: {error}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Sync working profiles with manager
            _workingProfiles.Clear();
            foreach (var kvp in TradeConfigManager.Instance.Trades)
            {
                _workingProfiles[kvp.Key] = kvp.Value.Clone();
            }
            _activeTradeCode = TradeConfigManager.Instance.ActiveTradeCode;
            _currentTradeCode = _activeTradeCode;

            RefreshProfileList();
            LoadWorkingProfileToEditor(_currentTradeCode);

            WpfMessageBox.Show("Trade profile(s) imported successfully!", "Import Successful",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnExportJson()
        {
            SaveCurrentEditorToWorkingProfile();

            if (string.IsNullOrWhiteSpace(_currentTradeCode) || !_workingProfiles.TryGetValue(_currentTradeCode!, out var profile))
                return;

            var sfd = new MicrosoftSaveFileDialog
            {
                Title = $"Export Trade Profile '{_currentTradeCode}' to JSON",
                Filter = "JSON File (*.json)|*.json",
                FileName = $"{profile.TradeCode}_Profile.json"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = System.Text.Json.JsonSerializer.Serialize(profile, options);
                File.WriteAllText(sfd.FileName, json);

                WpfMessageBox.Show($"Profile '{profile.TradeCode}' exported successfully to:\n{sfd.FileName}",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Export failed:\n{ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSetActive()
        {
            SaveCurrentEditorToWorkingProfile();

            if (!string.IsNullOrWhiteSpace(_currentTradeCode))
            {
                _activeTradeCode = _currentTradeCode!;
                RefreshProfileList();
                _txtStatus.Text = $"Active profile set to: [{_activeTradeCode}]";
            }
        }

        private void OnSaveAndClose()
        {
            SaveCurrentEditorToWorkingProfile();

            var configMgr = TradeConfigManager.Instance;
            configMgr.Trades.Clear();

            foreach (var kvp in _workingProfiles)
            {
                configMgr.Trades[kvp.Key] = kvp.Value;
            }

            configMgr.ActiveTradeCode = _activeTradeCode;
            configMgr.SaveToJson();

            DialogResult = true;
            Close();
        }

        // ════════════════════════════════════════════════════════════════
        // UI HELPERS
        // ════════════════════════════════════════════════════════════════

        private static TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(20, 60, 110)),
                Margin = new Thickness(0, 6, 0, 4)
            };
        }

        private static void AddFormRow(Grid grid, int row, string label, WpfTextBox textBox)
        {
            var lbl = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(60, 65, 75))
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            textBox.Margin = new Thickness(0, 2, 0, 2);
            textBox.Padding = new Thickness(4, 2, 4, 2);
            textBox.FontSize = 11.5;
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);
        }

        private static WpfButton MakeSmallButton(string text)
        {
            return new WpfButton
            {
                Content = text,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 11
            };
        }
    }
}
