using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevitLocationExporter.Configuration;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;

namespace RevitLocationExporter.Form
{
    /// <summary>
    /// Interactive WPF modal allowing the user to select operations to execute,
    /// configure global workset opening options for all models, and select
    /// which project locations to process per file.
    /// Also allows choosing or overriding the active Trade Profile.
    /// </summary>
    public sealed class LocationSelectionDialog : Window
    {
        private readonly Dictionary<string, Dictionary<string, WpfCheckBox>> _checkMap =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, WpfCheckBox> _wsNameCheckMap =
            new(StringComparer.OrdinalIgnoreCase);

        // Trade Profile Selection
        private readonly WpfComboBox _cbTradeProfile;

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

        // Workset Master Checkbox
        private readonly WpfCheckBox _cbAllTradeWorksets;

        public Dictionary<string, List<string>> SelectedLocations { get; private set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public WorksetSelectionOptions WorksetOptions { get; private set; } = new();

        public ExportExecutionOptions Options { get; private set; } = new();

        /// <summary>
        /// Explicit trade profile selected by user, or null if Auto-Detect was chosen.
        /// </summary>
        public TradeProfile? SelectedTradeOverride { get; private set; }

        public LocationSelectionDialog(
            Dictionary<string, List<string>> fileLocations,
            IEnumerable<string>? discoveredWorksetNames = null)
        {
            Title = "Export & Processing Options";
            Width = 760;
            MinHeight = 480;
            MaxHeight = SystemParameters.WorkArea.Height * 0.90;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = new SolidColorBrush(WpfColor.FromRgb(245, 245, 248));

            // Root layout
            var root = new DockPanel();
            Content = root;

            // ── Bottom button bar ──────────────────────────────────────────
            var btnBar = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(235, 235, 240)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(200, 200, 210)),
                Padding = new Thickness(14, 10, 14, 10)
            };
            DockPanel.SetDock(btnBar, Dock.Bottom);

            var btnPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };

            var okBtn = new WpfButton
            {
                Content = "Run Selected Operations",
                IsDefault = true,
                Padding = new Thickness(18, 6, 18, 6),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 160,
                Background = new SolidColorBrush(WpfColor.FromRgb(0, 120, 215)),
                Foreground = WpfBrushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold
            };
            okBtn.Click += OnOk;

            var cancelBtn = new WpfButton
            {
                Content = "Cancel",
                IsCancel = true,
                Padding = new Thickness(16, 6, 16, 6),
                MinWidth = 80
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; };

            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            btnBar.Child = btnPanel;
            root.Children.Add(btnBar);

            // ── Scrollable body ────────────────────────────────────────────
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(14, 12, 14, 12)
            };
            DockPanel.SetDock(scroll, Dock.Top);

            var body = new StackPanel();
            scroll.Content = body;
            root.Children.Add(scroll);

            // ════════════════════════════════════════════════════════════════
            // 0. TRADE PROFILE SELECTION CARD
            // ════════════════════════════════════════════════════════════════
            var tradeCard = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(170, 195, 225)),
                Background = new SolidColorBrush(WpfColor.FromRgb(242, 247, 255)),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(4)
            };
            var tradeDock = new DockPanel();
            tradeCard.Child = tradeDock;
            body.Children.Add(tradeCard);

            var btnManageTrades = new WpfButton
            {
                Content = "Manage / Build Profiles...",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(10, 0, 0, 0),
                Background = new SolidColorBrush(WpfColor.FromRgb(235, 240, 250)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(160, 185, 220)),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                ToolTip = "Build or customize trade profiles, workset rules, Revit categories, and file naming templates."
            };
            btnManageTrades.Click += (s, e) =>
            {
                var mgrDlg = new TradeProfileManagerDialog();
                if (mgrDlg.ShowDialog() == true)
                {
                    PopulateTradeComboBox();
                }
            };
            DockPanel.SetDock(btnManageTrades, Dock.Right);
            tradeDock.Children.Add(btnManageTrades);

            var tradeLabel = new TextBlock
            {
                Text = "Trade Profile:",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0, 60, 120))
            };
            DockPanel.SetDock(tradeLabel, Dock.Left);
            tradeDock.Children.Add(tradeLabel);

            _cbTradeProfile = new WpfComboBox
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Select an explicit trade profile or keep Auto-Detect to identify trade from each model's filename."
            };
            PopulateTradeComboBox();
            tradeDock.Children.Add(_cbTradeProfile);

            // ════════════════════════════════════════════════════════════════
            // 1. OPERATIONS & EXPORT OPTIONS CARD
            // ════════════════════════════════════════════════════════════════
            var opsCard = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(180, 200, 230)),
                Background = new SolidColorBrush(WpfColor.FromRgb(250, 252, 255)),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(12, 10, 12, 12),
                CornerRadius = new CornerRadius(4)
            };
            var opsStack = new StackPanel();
            opsCard.Child = opsStack;
            body.Children.Add(opsCard);

            // Operations Header Row
            var opsHeaderRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var opsTitle = new TextBlock
            {
                Text = "1. Operations to Execute",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0, 70, 140)),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(opsTitle, Dock.Left);

            // Preset Buttons Panel
            var presetPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(presetPanel, Dock.Right);

            var btnStandard = MakeQuickButton("Standard");
            var btnAll = MakeQuickButton("All");
            var btnExportsOnly = MakeQuickButton("Exports Only");
            var btnParamsOnly = MakeQuickButton("Params Only");

            presetPanel.Children.Add(btnStandard);
            presetPanel.Children.Add(btnAll);
            presetPanel.Children.Add(btnExportsOnly);
            presetPanel.Children.Add(btnParamsOnly);

            opsHeaderRow.Children.Add(presetPanel);
            opsHeaderRow.Children.Add(opsTitle);
            opsStack.Children.Add(opsHeaderRow);

            // Checkbox 3-Column Grid
            var opsGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            opsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Column 1: Setup & Parameters
            var col1 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            col1.Children.Add(new TextBlock
            {
                Text = "Data & Setup",
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 100, 110)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            _cbFillParams = MakeCheckBox("Fill CPK & Sheet Params", true);
            _cbCoordWorksets = MakeCheckBox("Coordinates & Worksets", true);
            _cbPurge = MakeCheckBox("Purge Unused Elements", true);
            // Model Checker standards: OFF by default, only available with "Fill CPK & Sheet Params"
            _cbModelStandards = MakeCheckBox("Model Checker Standards", false);
            _cbModelStandards.Margin = new Thickness(18, 0, 0, 2);
            _cbModelStandards.ToolTip =
                "Fill empty parameters / fix worksets required by the Model Checker XMLs\n" +
                "(ModelCheckers\\_ALL + ModelCheckers\\<Trade>). Writes a CSV report.\n" +
                "Runs after sheet updates, before Purge and exports.";
            _cbStandardsDryRun = MakeCheckBox("Dry run (report only)", false);
            _cbStandardsDryRun.Margin = new Thickness(36, 0, 0, 4);
            _cbStandardsDryRun.ToolTip = "Evaluate and write the CSV report, but roll back all model changes.";

            col1.Children.Add(_cbFillParams);
            col1.Children.Add(_cbModelStandards);
            col1.Children.Add(_cbStandardsDryRun);
            col1.Children.Add(_cbCoordWorksets);
            col1.Children.Add(_cbPurge);

            // Enable chain: Fill Params -> Model Checker Standards -> Dry run
            void SyncStandardsEnabled()
            {
                _cbModelStandards.IsEnabled = _cbFillParams.IsChecked == true;
                _cbStandardsDryRun.IsEnabled = _cbModelStandards.IsEnabled && _cbModelStandards.IsChecked == true;
            }
            _cbFillParams.Checked += (s, e) => SyncStandardsEnabled();
            _cbFillParams.Unchecked += (s, e) => SyncStandardsEnabled();
            _cbModelStandards.Checked += (s, e) => SyncStandardsEnabled();
            _cbModelStandards.Unchecked += (s, e) => SyncStandardsEnabled();
            SyncStandardsEnabled();
            Grid.SetColumn(col1, 0);
            opsGrid.Children.Add(col1);

            // Column 2: 3D Model Exports
            var col2 = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            col2.Children.Add(new TextBlock
            {
                Text = "3D & Central Exports",
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 100, 110)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            _cbNwc = MakeCheckBox("Export NWC (.nwc)", true);
            _cbIfc = MakeCheckBox("Export IFC (.ifc)", true);
            _cbRvt = MakeCheckBox("Save Central RVT (.rvt)", true);
            col2.Children.Add(_cbNwc);
            col2.Children.Add(_cbIfc);
            col2.Children.Add(_cbRvt);
            Grid.SetColumn(col2, 1);
            opsGrid.Children.Add(col2);

            // Column 3: 2D Sheet Exports
            var col3 = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            col3.Children.Add(new TextBlock
            {
                Text = "2D Sheet Exports",
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 100, 110)),
                Margin = new Thickness(0, 0, 0, 4)
            });
            _cbPdf = MakeCheckBox("Export Sheet PDF (.pdf)", false);
            _cbDwg = MakeCheckBox("Export Sheet DWG (.dwg)", false);
            col3.Children.Add(_cbPdf);
            col3.Children.Add(_cbDwg);
            Grid.SetColumn(col3, 2);
            opsGrid.Children.Add(col3);

            opsStack.Children.Add(opsGrid);

            // Preset handlers
            btnStandard.Click += (s, e) =>
            {
                _cbFillParams.IsChecked = true;
                _cbCoordWorksets.IsChecked = true;
                _cbPurge.IsChecked = true;
                _cbNwc.IsChecked = true;
                _cbIfc.IsChecked = true;
                _cbRvt.IsChecked = true;
                _cbPdf.IsChecked = false;
                _cbDwg.IsChecked = false;
            };

            btnAll.Click += (s, e) =>
            {
                _cbFillParams.IsChecked = true;
                _cbCoordWorksets.IsChecked = true;
                _cbPurge.IsChecked = true;
                _cbNwc.IsChecked = true;
                _cbIfc.IsChecked = true;
                _cbRvt.IsChecked = true;
                _cbPdf.IsChecked = true;
                _cbDwg.IsChecked = true;
            };

            btnExportsOnly.Click += (s, e) =>
            {
                _cbFillParams.IsChecked = false;
                _cbCoordWorksets.IsChecked = false;
                _cbPurge.IsChecked = false;
                _cbNwc.IsChecked = true;
                _cbIfc.IsChecked = true;
                _cbRvt.IsChecked = true;
                _cbPdf.IsChecked = true;
                _cbDwg.IsChecked = true;
            };

            btnParamsOnly.Click += (s, e) =>
            {
                _cbFillParams.IsChecked = true;
                _cbCoordWorksets.IsChecked = true;
                _cbPurge.IsChecked = true;
                _cbNwc.IsChecked = false;
                _cbIfc.IsChecked = false;
                _cbRvt.IsChecked = false;
                _cbPdf.IsChecked = false;
                _cbDwg.IsChecked = false;
            };

            // ════════════════════════════════════════════════════════════════
            // 2. GLOBAL WORKSETS TO OPEN (APPLIES TO ALL SELECTED MODELS)
            // ════════════════════════════════════════════════════════════════
            var wsCard = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(190, 205, 220)),
                Background = new SolidColorBrush(WpfColor.FromRgb(252, 253, 255)),
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(12, 10, 12, 12),
                CornerRadius = new CornerRadius(4)
            };
            var wsStack = new StackPanel();
            wsCard.Child = wsStack;
            body.Children.Add(wsCard);

            // Workset Header Row
            var wsHeaderRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var wsTitle = new TextBlock
            {
                Text = "2. Worksets to Open (Applies to All Models)",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(20, 60, 100)),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(wsTitle, Dock.Left);

            var wsPresetPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(wsPresetPanel, Dock.Right);

            var btnAllWs = MakeQuickButton("All Worksets");
            var btnTradeWs = MakeQuickButton("Trade Only");
            var btnNoneWs = MakeQuickButton("None");

            wsPresetPanel.Children.Add(btnAllWs);
            wsPresetPanel.Children.Add(btnTradeWs);
            wsPresetPanel.Children.Add(btnNoneWs);

            wsHeaderRow.Children.Add(wsPresetPanel);
            wsHeaderRow.Children.Add(wsTitle);
            wsStack.Children.Add(wsHeaderRow);

            var wsSubtitle = new TextBlock
            {
                Text = "Global workset settings applied to every selected model. Worksets not present in a file are safely skipped.",
                FontSize = 11,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(110, 115, 130)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            wsStack.Children.Add(wsSubtitle);

            // Master Trade-Specific Worksets Checkbox
            _cbAllTradeWorksets = new WpfCheckBox
            {
                Content = "All Trade-Specific Worksets (auto-matches workset tokens for the model's trade profile)",
                IsChecked = true,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            wsStack.Children.Add(_cbAllTradeWorksets);

            // Discovered Individual Workset Names
            var uniqueWorksets = discoveredWorksetNames != null
                ? discoveredWorksetNames.Where(s => !string.IsNullOrWhiteSpace(s)).OrderBy(s => s).ToList()
                : new List<string>();

            if (uniqueWorksets.Count > 0)
            {
                wsStack.Children.Add(new Separator
                {
                    Margin = new Thickness(0, 4, 0, 6),
                    Background = new SolidColorBrush(WpfColor.FromRgb(230, 235, 245))
                });

                var wsListHeader = new TextBlock
                {
                    Text = "Specific Workset Names Discovered Across Selected Models:",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(80, 85, 95)),
                    Margin = new Thickness(0, 0, 0, 4)
                };
                wsStack.Children.Add(wsListHeader);

                var wsWrap = new WrapPanel { Orientation = WpfOrientation.Horizontal };
                foreach (string wsName in uniqueWorksets)
                {
                    var cbWs = new WpfCheckBox
                    {
                        Content = new TextBlock { Text = wsName },
                        IsChecked = true, // Default to checked so all worksets open unless Trade Only is selected
                        Margin = new Thickness(0, 2, 16, 3),
                        MinWidth = 130,
                        FontSize = 11,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        ToolTip = $"Open '{wsName}' across all models where present"
                    };
                    _wsNameCheckMap[wsName] = cbWs;
                    wsWrap.Children.Add(cbWs);
                }
                wsStack.Children.Add(wsWrap);
            }
            else
            {
                var noWsNotice = new TextBlock
                {
                    Text = "No user worksets detected in selected files (non-workshared models or single workset).",
                    FontStyle = FontStyles.Italic,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(140, 140, 150)),
                    Margin = new Thickness(0, 2, 0, 2)
                };
                wsStack.Children.Add(noWsNotice);
            }

            // Workset Preset Handlers
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
                    // In Trade Only mode, keep Shared Levels/Grids checked, uncheck non-trade worksets
                    bool isCoordination = kv.Key.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         kv.Key.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0;
                    kv.Value.IsChecked = isCoordination;
                }
            };

            btnNoneWs.Click += (s, e) =>
            {
                _cbAllTradeWorksets.IsChecked = false;
                foreach (var cb in _wsNameCheckMap.Values) cb.IsChecked = false;
            };

            // ════════════════════════════════════════════════════════════════
            // 3. FILE & LOCATION SELECTION CARDS
            // ════════════════════════════════════════════════════════════════
            body.Children.Add(new TextBlock
            {
                Text = "3. Select Locations to Process",
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(40, 40, 50)),
                Margin = new Thickness(0, 4, 0, 8)
            });

            foreach (var kvp in fileLocations)
            {
                string filePath = kvp.Key;
                var locationNames = kvp.Value;

                var fileCheckBoxes = new Dictionary<string, WpfCheckBox>(StringComparer.OrdinalIgnoreCase);
                _checkMap[filePath] = fileCheckBoxes;

                var card = new Border
                {
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(WpfColor.FromRgb(200, 200, 215)),
                    Background = WpfBrushes.White,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(10, 8, 10, 10),
                    CornerRadius = new CornerRadius(3)
                };
                var cardStack = new StackPanel();
                card.Child = cardStack;
                body.Children.Add(card);

                // File header row
                var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

                var fileLabel = new TextBlock
                {
                    Text = Path.GetFileName(filePath),
                    FontWeight = FontWeights.Bold,
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = filePath,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(fileLabel, Dock.Left);

                // Quick buttons for locations in this file
                var locQuickPanel = new StackPanel
                {
                    Orientation = WpfOrientation.Horizontal,
                    HorizontalAlignment = WpfHorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(locQuickPanel, Dock.Right);

                var allLocBtn = MakeQuickButton("All");
                var noneLocBtn = MakeQuickButton("None");

                var capturedMap = fileCheckBoxes;
                allLocBtn.Click += (s, e) =>
                {
                    foreach (var cb in capturedMap.Values) cb.IsChecked = true;
                };
                noneLocBtn.Click += (s, e) =>
                {
                    foreach (var cb in capturedMap.Values) cb.IsChecked = false;
                };

                locQuickPanel.Children.Add(allLocBtn);
                locQuickPanel.Children.Add(noneLocBtn);

                headerRow.Children.Add(locQuickPanel);
                headerRow.Children.Add(fileLabel);
                cardStack.Children.Add(headerRow);

                // Separator
                cardStack.Children.Add(new Separator
                {
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = new SolidColorBrush(WpfColor.FromRgb(225, 225, 235))
                });

                // Location checkboxes
                var wrapPanel = new WrapPanel { Orientation = WpfOrientation.Horizontal };
                foreach (string loc in locationNames)
                {
                    var cb = new WpfCheckBox
                    {
                        Content = new TextBlock { Text = loc },
                        IsChecked = true,
                        Margin = new Thickness(0, 3, 16, 3),
                        MinWidth = 110,
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    fileCheckBoxes[loc] = cb;
                    wrapPanel.Children.Add(cb);
                }

                cardStack.Children.Add(wrapPanel);
            }
        }

        private void PopulateTradeComboBox()
        {
            _cbTradeProfile.Items.Clear();

            _cbTradeProfile.Items.Add(new ComboBoxItem
            {
                Content = "Auto-Detect from File Name (Default)",
                Tag = null
            });

            var configMgr = TradeConfigManager.Instance;
            int selectIdx = 0;
            int idx = 1;

            foreach (var kvp in configMgr.Trades.OrderBy(k => k.Key))
            {
                var item = new ComboBoxItem
                {
                    Content = $"[{kvp.Key}] {kvp.Value.TradeName}",
                    Tag = kvp.Value
                };
                _cbTradeProfile.Items.Add(item);

                if (string.Equals(kvp.Key, configMgr.ActiveTradeCode, StringComparison.OrdinalIgnoreCase))
                {
                    selectIdx = idx;
                }
                idx++;
            }

            _cbTradeProfile.SelectedIndex = selectIdx >= 0 && selectIdx < _cbTradeProfile.Items.Count ? selectIdx : 0;
        }

        private static WpfCheckBox MakeCheckBox(string text, bool isChecked) =>
            new WpfCheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin = new Thickness(0, 3, 0, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 11.5
            };

        private static WpfButton MakeQuickButton(string text) =>
            new WpfButton
            {
                Content = text,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(3, 0, 0, 0),
                FontSize = 10,
                MinWidth = 36,
                Background = new SolidColorBrush(WpfColor.FromRgb(235, 235, 242)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(185, 185, 200))
            };

        private void OnOk(object sender, RoutedEventArgs e)
        {
            // 1. Validate operations
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
                WpfMessageBox.Show(
                    "Please select at least one operation to execute.",
                    "No Operations Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 2. Validate locations
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
                WpfMessageBox.Show(
                    "No locations selected. Please check at least one location.",
                    "No Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 3. Record global workset options
            WorksetOptions.OpenTradeWorksets = _cbAllTradeWorksets.IsChecked == true;
            WorksetOptions.SelectedWorksetNames = _wsNameCheckMap
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool allWsChecked = _cbAllTradeWorksets.IsChecked == true &&
                                (_wsNameCheckMap.Count == 0 || _wsNameCheckMap.Values.All(cb => cb.IsChecked == true));
            WorksetOptions.OpenAllWorksets = allWsChecked;

            // 4. Record selected execution options
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

            // 5. Record selected trade profile override
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
