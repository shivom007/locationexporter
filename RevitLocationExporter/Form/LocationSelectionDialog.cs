using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RevitLocationExporter.Configuration;
using RevitLocationExporter.Core;
using MicrosoftOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace RevitLocationExporter.Form
{
    /// <summary>
    /// Modern Fluent run dialog for Location Exporter:
    /// displays the detected/selected Trade Profile, its synced Excel status (eliminating repetitive file pickers),
    /// execution toggles pre-loaded from trade defaults, workset filters, and searchable model locations.
    /// </summary>
    public sealed class LocationSelectionDialog : Window
    {
        private readonly Dictionary<string, List<string>> _fileLocations;
        private readonly Dictionary<string, Dictionary<string, WpfCheckBox>> _checkMap =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, WpfCheckBox> _wsNameCheckMap =
            new(StringComparer.OrdinalIgnoreCase);

        // Header & Sync Bar
        private readonly WpfComboBox _cbTradeProfile;
        private readonly Border _excelStatusPill;
        private readonly TextBlock _txtExcelPathInfo;
        private readonly WpfButton _btnSyncNow;
        private readonly WpfButton _btnClearOverride;

        // Operation Checkboxes
        private readonly WpfCheckBox _cbFillParams;
        private readonly WpfCheckBox _cbCoordWorksets;
        private readonly WpfCheckBox _cbPurge;
        private readonly WpfCheckBox _cbNwc;
        private readonly WpfCheckBox _cbIfc;
        private readonly WpfCheckBox _cbRvt;
        private readonly WpfCheckBox _cbPdf;
        private readonly WpfCheckBox _cbDwg;
        private readonly WpfCheckBox _cbModelStandards;
        private readonly WpfCheckBox _cbStandardsDryRun;

        // Worksets & Locations
        private readonly WpfCheckBox _cbAllTradeWorksets;
        private readonly WpfTextBox _tbLocationSearch;
        private readonly TextBlock _txtLocBadge;
        private readonly TextBlock _txtBottomSummary;
        private readonly StackPanel _locationsHost;

        public Dictionary<string, List<string>> SelectedLocations { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public WorksetSelectionOptions WorksetOptions { get; private set; } = new();

        public ExportExecutionOptions Options { get; private set; } = new();

        public TradeProfile? SelectedTradeOverride { get; private set; }

        public string? OneOffExcelOverridePath { get; private set; }

        public LocationSelectionDialog(
            Dictionary<string, List<string>> fileLocations,
            IEnumerable<string>? discoveredWorksetNames = null)
        {
            _fileLocations = fileLocations ?? new Dictionary<string, List<string>>();

            Title = "Location Exporter — Run Configuration";
            Width = 900;
            Height = 670;
            MinWidth = 780;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            UiTheme.ApplyTheme(this);

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

            var tradeControls = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(tradeControls, Dock.Right);

            tradeControls.Children.Add(new TextBlock
            {
                Text = "Trade Profile:",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });

            _cbTradeProfile = new WpfComboBox
            {
                Style = (Style)Resources["ModernComboBox"],
                Width = 230,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var btnManageTrades = new WpfButton
            {
                Content = "Manage Profiles...",
                Style = (Style)Resources["SecondaryButton"],
                Padding = new Thickness(12, 6, 12, 6)
            };
            btnManageTrades.Click += (s, e) =>
            {
                string? initialCode = ResolveEffectiveTrade()?.TradeCode;
                var mgrDlg = new TradeProfileManagerDialog(initialCode);
                if (mgrDlg.ShowDialog() == true)
                {
                    PopulateTradeComboBox();
                    ApplyTradeDefaultsAndSyncBar();
                }
            };

            tradeControls.Children.Add(_cbTradeProfile);
            tradeControls.Children.Add(btnManageTrades);
            headerDock.Children.Add(tradeControls);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Location Exporter",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextPrimaryBrush
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = $"{_fileLocations.Count} model(s) queued — review synced parameters, operations, and locations",
                FontSize = 11.5,
                Foreground = UiTheme.TextMutedBrush,
                Margin = new Thickness(0, 2, 0, 0)
            });
            headerDock.Children.Add(titleStack);
            root.Children.Add(headerBar);

            // ── SYNCED EXCEL STATUS BAR ──────────────────────────────────────
            var syncBar = new Border
            {
                Background = UiTheme.SubtleSurfaceBrush,
                BorderBrush = UiTheme.BorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(18, 8, 18, 8)
            };
            DockPanel.SetDock(syncBar, Dock.Top);
            var syncDock = new DockPanel();
            syncBar.Child = syncDock;

            var syncBtns = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(syncBtns, Dock.Right);

            _btnSyncNow = new WpfButton
            {
                Content = "Sync Now",
                Style = (Style)Resources["SmallButton"],
                Margin = new Thickness(8, 0, 6, 0)
            };
            _btnSyncNow.Click += (s, e) => OnSyncNowClicked();

            var btnOverrideExcel = new WpfButton
            {
                Content = "Override Excel...",
                Style = (Style)Resources["SmallButton"]
            };
            btnOverrideExcel.Click += (s, e) => OnOverrideExcelClicked();

            _btnClearOverride = new WpfButton
            {
                Content = "Clear",
                Style = (Style)Resources["GhostButton"],
                Margin = new Thickness(4, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _btnClearOverride.Click += (s, e) =>
            {
                OneOffExcelOverridePath = null;
                RefreshExcelStatusBar();
            };

            syncBtns.Children.Add(_btnSyncNow);
            syncBtns.Children.Add(btnOverrideExcel);
            syncBtns.Children.Add(_btnClearOverride);
            syncDock.Children.Add(syncBtns);

            var syncLeft = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _excelStatusPill = UiTheme.MakeStatusPill("Not linked", StatusTone.Neutral);
            _excelStatusPill.Margin = new Thickness(0, 0, 10, 0);

            _txtExcelPathInfo = new TextBlock
            {
                FontSize = 12,
                Foreground = UiTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            syncLeft.Children.Add(_excelStatusPill);
            syncLeft.Children.Add(_txtExcelPathInfo);
            syncDock.Children.Add(syncLeft);
            root.Children.Add(syncBar);

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

            var cancelBtn = new WpfButton
            {
                Content = "Cancel",
                IsCancel = true,
                Style = (Style)Resources["SecondaryButton"],
                MinWidth = 84,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; };

            var okBtn = new WpfButton
            {
                Content = "Run Export",
                IsDefault = true,
                Style = (Style)Resources["PrimaryButton"],
                MinWidth = 120
            };
            okBtn.Click += OnOk;

            footerBtns.Children.Add(cancelBtn);
            footerBtns.Children.Add(okBtn);
            footerDock.Children.Add(footerBtns);

            _txtBottomSummary = new TextBlock
            {
                FontSize = 12,
                Foreground = UiTheme.TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            footerDock.Children.Add(_txtBottomSummary);
            root.Children.Add(footerBar);

            // ── TWO-COLUMN MAIN BODY ─────────────────────────────────────────
            var mainGrid = new Grid { Margin = new Thickness(14) };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(mainGrid);

            // ── LEFT COLUMN: OPERATIONS + WORKSETS ───────────────────────────
            var leftDock = new DockPanel();
            Grid.SetColumn(leftDock, 0);
            mainGrid.Children.Add(leftDock);

            // Card 1: Operations
            var opsCard = UiTheme.MakeCard(new Thickness(14), new Thickness(0, 0, 0, 12));
            DockPanel.SetDock(opsCard, Dock.Top);
            var opsStack = new StackPanel();
            opsCard.Child = opsStack;

            var opsHeadDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var presetStack = new StackPanel { Orientation = WpfOrientation.Horizontal };
            DockPanel.SetDock(presetStack, Dock.Right);

            var btnStandard = new WpfButton { Content = "Standard", Style = (Style)Resources["SmallButton"], Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 4, 0) };
            var btnAll = new WpfButton { Content = "All", Style = (Style)Resources["SmallButton"], Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 4, 0) };
            var btnExportsOnly = new WpfButton { Content = "Exports", Style = (Style)Resources["SmallButton"], Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 4, 0) };
            var btnParamsOnly = new WpfButton { Content = "Params", Style = (Style)Resources["SmallButton"], Padding = new Thickness(7, 3, 7, 3) };

            presetStack.Children.Add(btnStandard);
            presetStack.Children.Add(btnAll);
            presetStack.Children.Add(btnExportsOnly);
            presetStack.Children.Add(btnParamsOnly);
            opsHeadDock.Children.Add(presetStack);
            opsHeadDock.Children.Add(new TextBlock
            {
                Text = "Operations",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            opsStack.Children.Add(opsHeadDock);

            var opsGrid = new Grid();
            opsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var opCol1 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            opCol1.Children.Add(MakeGroupHeader("Data & Standards"));
            _cbFillParams = MakeOpCheck("Fill Params & Rules", true);
            _cbModelStandards = MakeOpCheck("Model Standards", false);
            _cbModelStandards.Margin = new Thickness(18, 0, 0, 5);
            _cbStandardsDryRun = MakeOpCheck("Dry run (report)", false);
            _cbStandardsDryRun.Margin = new Thickness(34, 0, 0, 5);
            _cbCoordWorksets = MakeOpCheck("Coords & Worksets", true);
            _cbPurge = MakeOpCheck("Purge Unused", true);

            opCol1.Children.Add(_cbFillParams);
            opCol1.Children.Add(_cbModelStandards);
            opCol1.Children.Add(_cbStandardsDryRun);
            opCol1.Children.Add(_cbCoordWorksets);
            opCol1.Children.Add(_cbPurge);
            Grid.SetColumn(opCol1, 0);
            opsGrid.Children.Add(opCol1);

            var opCol2 = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            opCol2.Children.Add(MakeGroupHeader("Deliverables"));
            _cbNwc = MakeOpCheck("Export NWC (.nwc)", true);
            _cbIfc = MakeOpCheck("Export IFC (.ifc)", true);
            _cbRvt = MakeOpCheck("Save Central RVT", true);
            _cbPdf = MakeOpCheck("Export Sheet PDF", false);
            _cbDwg = MakeOpCheck("Export Sheet DWG", false);

            opCol2.Children.Add(_cbNwc);
            opCol2.Children.Add(_cbIfc);
            opCol2.Children.Add(_cbRvt);
            opCol2.Children.Add(_cbPdf);
            opCol2.Children.Add(_cbDwg);
            Grid.SetColumn(opCol2, 1);
            opsGrid.Children.Add(opCol2);

            opsStack.Children.Add(opsGrid);
            leftDock.Children.Add(opsCard);

            void SyncStandardsEnabled()
            {
                _cbModelStandards.IsEnabled = _cbFillParams.IsChecked == true;
                _cbStandardsDryRun.IsEnabled = _cbModelStandards.IsEnabled && _cbModelStandards.IsChecked == true;
            }
            _cbFillParams.Checked += (s, e) => SyncStandardsEnabled();
            _cbFillParams.Unchecked += (s, e) => SyncStandardsEnabled();
            _cbModelStandards.Checked += (s, e) => SyncStandardsEnabled();
            _cbModelStandards.Unchecked += (s, e) => SyncStandardsEnabled();

            btnStandard.Click += (s, e) => SetOps(true, true, true, true, true, true, false, false, false);
            btnAll.Click += (s, e) => SetOps(true, true, true, true, true, true, true, true, true);
            btnExportsOnly.Click += (s, e) => SetOps(false, false, false, true, true, true, true, true, false);
            btnParamsOnly.Click += (s, e) => SetOps(true, true, true, false, false, false, false, false, false);

            // Card 2: Worksets
            var wsCard = UiTheme.MakeCard(new Thickness(14), new Thickness(0));
            var wsDock = new DockPanel();
            wsCard.Child = wsDock;

            var wsHeadDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(wsHeadDock, Dock.Top);
            var wsBtns = new StackPanel { Orientation = WpfOrientation.Horizontal };
            DockPanel.SetDock(wsBtns, Dock.Right);
            var btnAllWs = new WpfButton { Content = "All", Style = (Style)Resources["SmallButton"], Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 4, 0) };
            var btnTradeWs = new WpfButton { Content = "Trade", Style = (Style)Resources["SmallButton"], Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 4, 0) };
            var btnNoneWs = new WpfButton { Content = "None", Style = (Style)Resources["SmallButton"], Padding = new Thickness(7, 3, 7, 3) };
            wsBtns.Children.Add(btnAllWs);
            wsBtns.Children.Add(btnTradeWs);
            wsBtns.Children.Add(btnNoneWs);
            wsHeadDock.Children.Add(wsBtns);
            wsHeadDock.Children.Add(new TextBlock
            {
                Text = "Worksets to Open",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            wsDock.Children.Add(wsHeadDock);

            _cbAllTradeWorksets = new WpfCheckBox
            {
                Content = "Auto-match trade worksets",
                IsChecked = true,
                Style = (Style)Resources["ModernCheckBox"],
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(_cbAllTradeWorksets, Dock.Top);
            wsDock.Children.Add(_cbAllTradeWorksets);

            var uniqueWorksets = discoveredWorksetNames != null
                ? discoveredWorksetNames.Where(s => !string.IsNullOrWhiteSpace(s)).OrderBy(s => s).ToList()
                : new List<string>();

            var wsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var wsListPanel = new StackPanel();
            if (uniqueWorksets.Count > 0)
            {
                foreach (string wsName in uniqueWorksets)
                {
                    var cbWs = new WpfCheckBox
                    {
                        Content = wsName,
                        IsChecked = true,
                        Style = (Style)Resources["ModernCheckBox"],
                        Margin = new Thickness(0, 2, 0, 4)
                    };
                    _wsNameCheckMap[wsName] = cbWs;
                    wsListPanel.Children.Add(cbWs);
                }
            }
            else
            {
                wsListPanel.Children.Add(new TextBlock
                {
                    Text = "No user worksets detected in selected models.",
                    FontSize = 11.5,
                    Foreground = UiTheme.TextMutedBrush
                });
            }
            wsScroll.Content = wsListPanel;
            wsDock.Children.Add(wsScroll);

            btnAllWs.Click += (s, e) =>
            {
                _cbAllTradeWorksets.IsChecked = true;
                foreach (var cb in _wsNameCheckMap.Values) cb.IsChecked = true;
            };
            btnTradeWs.Click += (s, e) =>
            {
                _cbAllTradeWorksets.IsChecked = true;
                foreach (var kv in _wsNameCheckMap)
                {
                    bool isCoord = kv.Key.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   kv.Key.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0;
                    kv.Value.IsChecked = isCoord;
                }
            };
            btnNoneWs.Click += (s, e) =>
            {
                _cbAllTradeWorksets.IsChecked = false;
                foreach (var cb in _wsNameCheckMap.Values) cb.IsChecked = false;
            };

            leftDock.Children.Add(wsCard);

            // ── RIGHT COLUMN: MODELS & LOCATIONS ─────────────────────────────
            var locCard = UiTheme.MakeCard(new Thickness(14), new Thickness(0));
            Grid.SetColumn(locCard, 2);
            mainGrid.Children.Add(locCard);

            var locDock = new DockPanel();
            locCard.Child = locDock;

            var locHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(locHeader, Dock.Top);
            _txtLocBadge = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.AccentBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_txtLocBadge, Dock.Right);
            locHeader.Children.Add(_txtLocBadge);
            locHeader.Children.Add(new TextBlock
            {
                Text = "Models & Project Locations",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextPrimaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            locDock.Children.Add(locHeader);

            var locToolDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(locToolDock, Dock.Top);
            var locBtns = new StackPanel { Orientation = WpfOrientation.Horizontal };
            DockPanel.SetDock(locBtns, Dock.Right);
            var btnAllLocs = new WpfButton { Content = "Select All", Style = (Style)Resources["SmallButton"], Margin = new Thickness(8, 0, 4, 0) };
            var btnClearLocs = new WpfButton { Content = "Clear", Style = (Style)Resources["SmallButton"] };
            locBtns.Children.Add(btnAllLocs);
            locBtns.Children.Add(btnClearLocs);
            locToolDock.Children.Add(locBtns);

            _tbLocationSearch = new WpfTextBox
            {
                Style = (Style)Resources["ModernTextBox"],
                ToolTip = "Filter locations by code or file name..."
            };
            _tbLocationSearch.TextChanged += (s, e) => RenderLocationsList();
            locToolDock.Children.Add(_tbLocationSearch);
            locDock.Children.Add(locToolDock);

            var locScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _locationsHost = new StackPanel();
            locScroll.Content = _locationsHost;
            locDock.Children.Add(locScroll);

            // Build checkbox map for all files/locations once
            foreach (var kvp in _fileLocations)
            {
                var fileCheckBoxes = new Dictionary<string, WpfCheckBox>(StringComparer.OrdinalIgnoreCase);
                _checkMap[kvp.Key] = fileCheckBoxes;
                foreach (string loc in kvp.Value)
                {
                    var cb = new WpfCheckBox
                    {
                        Content = loc,
                        IsChecked = true,
                        Style = (Style)Resources["ModernCheckBox"],
                        FontFamily = UiTheme.MonoFont,
                        FontSize = 11.5,
                        Margin = new Thickness(0, 3, 14, 4),
                        MinWidth = 105
                    };
                    cb.Checked += (s, e) => UpdateLocationCounters();
                    cb.Unchecked += (s, e) => UpdateLocationCounters();
                    fileCheckBoxes[loc] = cb;
                }
            }

            btnAllLocs.Click += (s, e) =>
            {
                foreach (var map in _checkMap.Values)
                    foreach (var cb in map.Values) cb.IsChecked = true;
                UpdateLocationCounters();
            };
            btnClearLocs.Click += (s, e) =>
            {
                foreach (var map in _checkMap.Values)
                    foreach (var cb in map.Values) cb.IsChecked = false;
                UpdateLocationCounters();
            };

            RenderLocationsList();
            UpdateLocationCounters();

            _cbTradeProfile.SelectionChanged += (s, e) => ApplyTradeDefaultsAndSyncBar();
            PopulateTradeComboBox();
            ApplyTradeDefaultsAndSyncBar();
            SyncStandardsEnabled();
        }

        private void RenderLocationsList()
        {
            _locationsHost.Children.Clear();
            string query = (_tbLocationSearch.Text ?? string.Empty).Trim();

            foreach (var kvp in _fileLocations)
            {
                string filePath = kvp.Key;
                string fileName = Path.GetFileName(filePath);
                var map = _checkMap[filePath];

                bool fileMatch = string.IsNullOrEmpty(query) ||
                                 fileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                var matchingLocs = map.Where(locKv =>
                    fileMatch || locKv.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                if (matchingLocs.Count == 0) continue;

                var fileCard = UiTheme.MakeCard(new Thickness(10, 8, 10, 10), new Thickness(0, 0, 0, 8), subtle: true);
                var stack = new StackPanel();
                fileCard.Child = stack;

                var fileHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var fileBtns = new StackPanel { Orientation = WpfOrientation.Horizontal };
                DockPanel.SetDock(fileBtns, Dock.Right);

                var btnAllFile = new WpfButton { Content = "All", Style = (Style)Resources["SmallButton"], Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 0) };
                var btnNoneFile = new WpfButton { Content = "None", Style = (Style)Resources["SmallButton"], Padding = new Thickness(6, 2, 6, 2) };
                btnAllFile.Click += (s, e) => { foreach (var cb in map.Values) cb.IsChecked = true; UpdateLocationCounters(); };
                btnNoneFile.Click += (s, e) => { foreach (var cb in map.Values) cb.IsChecked = false; UpdateLocationCounters(); };
                fileBtns.Children.Add(btnAllFile);
                fileBtns.Children.Add(btnNoneFile);
                fileHead.Children.Add(fileBtns);

                fileHead.Children.Add(new TextBlock
                {
                    Text = fileName,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = UiTheme.TextPrimaryBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = filePath
                });
                stack.Children.Add(fileHead);

                var wrap = new WrapPanel { Orientation = WpfOrientation.Horizontal };
                foreach (var locKv in matchingLocs)
                {
                    if (locKv.Value.Parent is System.Windows.Controls.Panel oldParent)
                        oldParent.Children.Remove(locKv.Value);
                    wrap.Children.Add(locKv.Value);
                }
                stack.Children.Add(wrap);
                _locationsHost.Children.Add(fileCard);
            }
        }

        private void UpdateLocationCounters()
        {
            int selectedLocs = 0;
            int totalLocs = 0;
            int selectedFiles = 0;

            foreach (var map in _checkMap.Values)
            {
                totalLocs += map.Count;
                int fileSel = map.Values.Count(cb => cb.IsChecked == true);
                selectedLocs += fileSel;
                if (fileSel > 0) selectedFiles++;
            }

            _txtLocBadge.Text = $"{selectedLocs} / {totalLocs} selected";
            _txtBottomSummary.Text = $"{selectedLocs} location(s) across {selectedFiles} model(s) ready";
        }

        private void PopulateTradeComboBox()
        {
            _cbTradeProfile.Items.Clear();
            _cbTradeProfile.Items.Add(new ComboBoxItem
            {
                Content = "Auto-Detect from File Name",
                Tag = null
            });

            var configMgr = TradeConfigManager.Instance;
            int selectIdx = 0;
            int idx = 1;

            foreach (var kvp in configMgr.Trades.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                _cbTradeProfile.Items.Add(new ComboBoxItem
                {
                    Content = $"[{kvp.Key}] {kvp.Value.TradeName}",
                    Tag = kvp.Value
                });

                if (string.Equals(kvp.Key, configMgr.ActiveTradeCode, StringComparison.OrdinalIgnoreCase))
                    selectIdx = idx;
                idx++;
            }

            _cbTradeProfile.SelectedIndex = selectIdx >= 0 && selectIdx < _cbTradeProfile.Items.Count ? selectIdx : 0;
        }

        private TradeProfile? ResolveEffectiveTrade()
        {
            if (_cbTradeProfile.SelectedItem is ComboBoxItem cbi && cbi.Tag is TradeProfile explicitTrade)
                return explicitTrade;

            string? firstFile = _fileLocations.Keys.FirstOrDefault();
            return firstFile != null
                ? TradeConfigManager.Instance.DetectTrade(Path.GetFileName(firstFile))
                : TradeConfigManager.Instance.GetActiveTrade();
        }

        private void ApplyTradeDefaultsAndSyncBar()
        {
            var trade = ResolveEffectiveTrade();
            if (trade != null)
            {
                trade.EnsureInitialized();
                SetOps(
                    trade.DefaultFillParameters,
                    trade.DefaultAlignCoordinatesAndWorksets,
                    trade.DefaultPurgeUnused,
                    trade.DefaultExportNwc,
                    trade.DefaultExportIfc,
                    trade.DefaultSaveCentralRvt,
                    trade.DefaultExportPdf,
                    trade.DefaultExportDwg,
                    trade.DefaultEnforceModelStandards);
            }
            RefreshExcelStatusBar();
        }

        private void RefreshExcelStatusBar()
        {
            if (!string.IsNullOrWhiteSpace(OneOffExcelOverridePath))
            {
                UiTheme.UpdateStatusPill(_excelStatusPill, "One-off override", StatusTone.Info);
                _txtExcelPathInfo.Text = OneOffExcelOverridePath!;
                _btnClearOverride.Visibility = Visibility.Visible;
                _btnSyncNow.IsEnabled = false;
                return;
            }

            _btnClearOverride.Visibility = Visibility.Collapsed;
            var trade = ResolveEffectiveTrade();
            if (trade == null || string.IsNullOrWhiteSpace(trade.LinkedExcelPath))
            {
                UiTheme.UpdateStatusPill(_excelStatusPill, "Excel not linked", StatusTone.Neutral);
                _txtExcelPathInfo.Text = trade != null
                    ? $"No workbook linked to [{trade.TradeCode}] — using profile rules & defaults"
                    : "No workbook linked";
                _btnSyncNow.IsEnabled = false;
                return;
            }

            _btnSyncNow.IsEnabled = true;
            string fileName = Path.GetFileName(trade.LinkedExcelPath);

            if (!File.Exists(trade.LinkedExcelPath))
            {
                UiTheme.UpdateStatusPill(_excelStatusPill, "Excel missing", StatusTone.Danger);
                _txtExcelPathInfo.Text = trade.SyncedData != null
                    ? $"'{fileName}' unavailable on disk — using cached snapshot"
                    : $"'{trade.LinkedExcelPath}' not found";
            }
            else if (TradeExcelSyncService.IsExcelModifiedSinceSync(trade))
            {
                UiTheme.UpdateStatusPill(_excelStatusPill, "Modified (auto-sync on run)", StatusTone.Warning);
                _txtExcelPathInfo.Text = $"{fileName} — {trade.ParameterRules.Count} rules";
            }
            else
            {
                UiTheme.UpdateStatusPill(_excelStatusPill, "Synced", StatusTone.Success);
                int locCount = trade.SyncedData?.ElementParameters.Count ?? 0;
                _txtExcelPathInfo.Text = $"{fileName} — {trade.ParameterRules.Count} rule(s), {locCount} location(s)";
            }
        }

        private void OnSyncNowClicked()
        {
            var trade = ResolveEffectiveTrade();
            if (trade == null || string.IsNullOrWhiteSpace(trade.LinkedExcelPath)) return;

            var report = TradeExcelSyncService.SyncFromExcel(trade, trade.LinkedExcelPath);
            if (report.Success)
            {
                TradeConfigManager.Instance.SaveToJson();
                ApplyTradeDefaultsAndSyncBar();
                WpfMessageBox.Show(this, $"Synced [{trade.TradeCode}] from '{Path.GetFileName(trade.LinkedExcelPath)}':\n{report.Message}",
                    "Sync Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WpfMessageBox.Show(this, report.Message, "Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnOverrideExcelClicked()
        {
            var dlg = new MicrosoftOpenFileDialog
            {
                Title = "Select One-Off Excel or CSV Override",
                Filter = "Excel or CSV Files (*.xlsx;*.xlsm;*.csv)|*.xlsx;*.xlsm;*.csv|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                OneOffExcelOverridePath = dlg.FileName;
                RefreshExcelStatusBar();
            }
        }

        private void SetOps(bool fill, bool coords, bool purge, bool nwc, bool ifc, bool rvt, bool pdf, bool dwg, bool standards)
        {
            _cbFillParams.IsChecked = fill;
            _cbCoordWorksets.IsChecked = coords;
            _cbPurge.IsChecked = purge;
            _cbNwc.IsChecked = nwc;
            _cbIfc.IsChecked = ifc;
            _cbRvt.IsChecked = rvt;
            _cbPdf.IsChecked = pdf;
            _cbDwg.IsChecked = dwg;
            _cbModelStandards.IsChecked = standards;
            _cbModelStandards.IsEnabled = fill;
            _cbStandardsDryRun.IsEnabled = fill && standards;
        }

        private static TextBlock MakeGroupHeader(string text) => new()
        {
            Text = text.ToUpperInvariant(),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.TextMutedBrush,
            Margin = new Thickness(0, 0, 0, 6)
        };

        private WpfCheckBox MakeOpCheck(string text, bool isChecked) => new()
        {
            Content = text,
            IsChecked = isChecked,
            Style = (Style)Resources["ModernCheckBox"],
            Margin = new Thickness(0, 0, 0, 5)
        };

        private void OnOk(object sender, RoutedEventArgs e)
        {
            bool anyOpSelected = _cbFillParams.IsChecked == true ||
                                 _cbCoordWorksets.IsChecked == true ||
                                 _cbPurge.IsChecked == true ||
                                 _cbNwc.IsChecked == true ||
                                 _cbIfc.IsChecked == true ||
                                 _cbRvt.IsChecked == true ||
                                 _cbPdf.IsChecked == true ||
                                 _cbDwg.IsChecked == true;

            if (!anyOpSelected)
            {
                WpfMessageBox.Show(this, "Please select at least one operation to execute.",
                    "No Operations Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedLocations.Clear();
            foreach (var fileKv in _checkMap)
            {
                var chosen = fileKv.Value
                    .Where(locKv => locKv.Value.IsChecked == true)
                    .Select(locKv => locKv.Key)
                    .ToList();

                if (chosen.Count > 0)
                    SelectedLocations[fileKv.Key] = chosen;
            }

            if (SelectedLocations.Count == 0)
            {
                WpfMessageBox.Show(this, "No locations selected. Please check at least one location.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            WorksetOptions.OpenTradeWorksets = _cbAllTradeWorksets.IsChecked == true;
            WorksetOptions.SelectedWorksetNames = _wsNameCheckMap
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool allWsChecked = _cbAllTradeWorksets.IsChecked == true &&
                                (_wsNameCheckMap.Count == 0 || _wsNameCheckMap.Values.All(cb => cb.IsChecked == true));
            WorksetOptions.OpenAllWorksets = allWsChecked;

            Options.FillParameters = _cbFillParams.IsChecked == true;
            Options.AlignCoordinatesAndWorksets = _cbCoordWorksets.IsChecked == true;
            Options.PurgeUnused = _cbPurge.IsChecked == true;
            Options.ExportNwc = _cbNwc.IsChecked == true;
            Options.ExportIfc = _cbIfc.IsChecked == true;
            Options.SaveCentralRvt = _cbRvt.IsChecked == true;
            Options.ExportPdf = _cbPdf.IsChecked == true;
            Options.ExportDwg = _cbDwg.IsChecked == true;
            Options.EnforceModelStandards = _cbModelStandards.IsEnabled && _cbModelStandards.IsChecked == true;
            Options.ModelStandardsDryRun = Options.EnforceModelStandards && _cbStandardsDryRun.IsChecked == true;

            if (_cbTradeProfile.SelectedItem is ComboBoxItem cbi && cbi.Tag is TradeProfile tp)
            {
                SelectedTradeOverride = tp;
                TradeConfigManager.Instance.SetActiveTrade(tp.TradeCode);
            }
            else
            {
                SelectedTradeOverride = null;
            }

            DialogResult = true;
        }
    }
}
