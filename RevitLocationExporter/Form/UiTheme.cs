using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace RevitLocationExporter.Form
{
    public enum StatusTone
    {
        Neutral,
        Info,
        Success,
        Warning,
        Danger
    }

    /// <summary>
    /// Shared Light Fluent Engineering WPF design system for RevitLocationExporter dialogs.
    /// Provides calm neutral surfaces, crisp 1px hairlines, one cobalt accent, and custom control templates.
    /// </summary>
    internal static class UiTheme
    {
        // ── Palette ──────────────────────────────────────────────────────────
        public static readonly WpfColor CanvasColor = WpfColor.FromRgb(0xF4, 0xF6, 0xF9);
        public static readonly WpfColor SurfaceColor = WpfColor.FromRgb(0xFF, 0xFF, 0xFF);
        public static readonly WpfColor SubtleSurfaceColor = WpfColor.FromRgb(0xF8, 0xFA, 0xFC);
        public static readonly WpfColor BorderColor = WpfColor.FromRgb(0xE2, 0xE8, 0xF0);
        public static readonly WpfColor BorderHoverColor = WpfColor.FromRgb(0xCB, 0xD5, 0xE1);

        public static readonly WpfColor AccentColor = WpfColor.FromRgb(0x1D, 0x63, 0xD5);
        public static readonly WpfColor AccentHoverColor = WpfColor.FromRgb(0x17, 0x52, 0xB3);
        public static readonly WpfColor AccentSoftColor = WpfColor.FromRgb(0xEF, 0xF6, 0xFF);
        public static readonly WpfColor AccentBorderColor = WpfColor.FromRgb(0xBF, 0xDB, 0xFE);

        public static readonly WpfColor TextPrimaryColor = WpfColor.FromRgb(0x0F, 0x17, 0x2A);
        public static readonly WpfColor TextSecondaryColor = WpfColor.FromRgb(0x47, 0x55, 0x69);
        public static readonly WpfColor TextMutedColor = WpfColor.FromRgb(0x64, 0x74, 0x8B);

        public static readonly WpfColor SuccessColor = WpfColor.FromRgb(0x15, 0x80, 0x3D);
        public static readonly WpfColor SuccessSoftColor = WpfColor.FromRgb(0xEC, 0xFD, 0xF5);
        public static readonly WpfColor SuccessBorderColor = WpfColor.FromRgb(0xA7, 0xF3, 0xD0);

        public static readonly WpfColor WarningColor = WpfColor.FromRgb(0xB4, 0x53, 0x09);
        public static readonly WpfColor WarningSoftColor = WpfColor.FromRgb(0xFF, 0xFB, 0xEB);
        public static readonly WpfColor WarningBorderColor = WpfColor.FromRgb(0xFD, 0xE6, 0x8A);

        public static readonly WpfColor DangerColor = WpfColor.FromRgb(0xB9, 0x1C, 0x1C);
        public static readonly WpfColor DangerSoftColor = WpfColor.FromRgb(0xFE, 0xF2, 0xF2);
        public static readonly WpfColor DangerBorderColor = WpfColor.FromRgb(0xFE, 0xCA, 0xCA);

        public static readonly SolidColorBrush CanvasBrush = Freeze(new SolidColorBrush(CanvasColor));
        public static readonly SolidColorBrush SurfaceBrush = Freeze(new SolidColorBrush(SurfaceColor));
        public static readonly SolidColorBrush SubtleSurfaceBrush = Freeze(new SolidColorBrush(SubtleSurfaceColor));
        public static readonly SolidColorBrush BorderBrush = Freeze(new SolidColorBrush(BorderColor));
        public static readonly SolidColorBrush BorderHoverBrush = Freeze(new SolidColorBrush(BorderHoverColor));

        public static readonly SolidColorBrush AccentBrush = Freeze(new SolidColorBrush(AccentColor));
        public static readonly SolidColorBrush AccentSoftBrush = Freeze(new SolidColorBrush(AccentSoftColor));
        public static readonly SolidColorBrush AccentBorderBrush = Freeze(new SolidColorBrush(AccentBorderColor));

        public static readonly SolidColorBrush TextPrimaryBrush = Freeze(new SolidColorBrush(TextPrimaryColor));
        public static readonly SolidColorBrush TextSecondaryBrush = Freeze(new SolidColorBrush(TextSecondaryColor));
        public static readonly SolidColorBrush TextMutedBrush = Freeze(new SolidColorBrush(TextMutedColor));

        public static readonly SolidColorBrush SuccessBrush = Freeze(new SolidColorBrush(SuccessColor));
        public static readonly SolidColorBrush SuccessSoftBrush = Freeze(new SolidColorBrush(SuccessSoftColor));
        public static readonly SolidColorBrush SuccessBorderBrush = Freeze(new SolidColorBrush(SuccessBorderColor));

        public static readonly SolidColorBrush WarningBrush = Freeze(new SolidColorBrush(WarningColor));
        public static readonly SolidColorBrush WarningSoftBrush = Freeze(new SolidColorBrush(WarningSoftColor));
        public static readonly SolidColorBrush WarningBorderBrush = Freeze(new SolidColorBrush(WarningBorderColor));

        public static readonly SolidColorBrush DangerBrush = Freeze(new SolidColorBrush(DangerColor));
        public static readonly SolidColorBrush DangerSoftBrush = Freeze(new SolidColorBrush(DangerSoftColor));
        public static readonly SolidColorBrush DangerBorderBrush = Freeze(new SolidColorBrush(DangerBorderColor));

        public static readonly WpfFontFamily UiFont = new WpfFontFamily("Segoe UI");
        public static readonly WpfFontFamily MonoFont = new WpfFontFamily("Consolas");

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        private static ResourceDictionary? _cachedDictionary;

        public static void ApplyTheme(Window window)
        {
            window.Background = CanvasBrush;
            window.FontFamily = UiFont;
            window.FontSize = 12.5;
            window.Foreground = TextPrimaryBrush;
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);

            _cachedDictionary ??= BuildResourceDictionary();
            window.Resources.MergedDictionaries.Add(_cachedDictionary);
        }

        private static ResourceDictionary BuildResourceDictionary()
        {
            const string xaml = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">

    <Style x:Key=""BaseButton"" TargetType=""Button"">
        <Setter Property=""FontFamily"" Value=""Segoe UI"" />
        <Setter Property=""FontSize"" Value=""12.5"" />
        <Setter Property=""FontWeight"" Value=""SemiBold"" />
        <Setter Property=""Cursor"" Value=""Hand"" />
        <Setter Property=""FocusVisualStyle"" Value=""{x:Null}"" />
        <Setter Property=""Padding"" Value=""14,7"" />
        <Setter Property=""BorderThickness"" Value=""1"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""Button"">
                    <Border x:Name=""bd""
                            Background=""{TemplateBinding Background}""
                            BorderBrush=""{TemplateBinding BorderBrush}""
                            BorderThickness=""{TemplateBinding BorderThickness}""
                            CornerRadius=""5""
                            Padding=""{TemplateBinding Padding}"">
                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter TargetName=""bd"" Property=""Opacity"" Value=""0.48"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""PrimaryButton"" TargetType=""Button"" BasedOn=""{StaticResource BaseButton}"">
        <Setter Property=""Background"" Value=""#1D63D5"" />
        <Setter Property=""BorderBrush"" Value=""#1D63D5"" />
        <Setter Property=""Foreground"" Value=""#FFFFFF"" />
        <Style.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter Property=""Background"" Value=""#1752B3"" />
                <Setter Property=""BorderBrush"" Value=""#1752B3"" />
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
                <Setter Property=""Background"" Value=""#124396"" />
                <Setter Property=""BorderBrush"" Value=""#124396"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style x:Key=""SecondaryButton"" TargetType=""Button"" BasedOn=""{StaticResource BaseButton}"">
        <Setter Property=""Background"" Value=""#FFFFFF"" />
        <Setter Property=""BorderBrush"" Value=""#CBD5E1"" />
        <Setter Property=""Foreground"" Value=""#0F172A"" />
        <Style.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter Property=""Background"" Value=""#F8FAFC"" />
                <Setter Property=""BorderBrush"" Value=""#94A3B8"" />
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
                <Setter Property=""Background"" Value=""#F1F5F9"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style x:Key=""GhostButton"" TargetType=""Button"" BasedOn=""{StaticResource BaseButton}"">
        <Setter Property=""Background"" Value=""Transparent"" />
        <Setter Property=""BorderBrush"" Value=""Transparent"" />
        <Setter Property=""Foreground"" Value=""#475569"" />
        <Setter Property=""Padding"" Value=""10,6"" />
        <Style.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter Property=""Background"" Value=""#E2E8F0"" />
                <Setter Property=""Foreground"" Value=""#0F172A"" />
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
                <Setter Property=""Background"" Value=""#CBD5E1"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style x:Key=""DangerButton"" TargetType=""Button"" BasedOn=""{StaticResource BaseButton}"">
        <Setter Property=""Background"" Value=""#FFFFFF"" />
        <Setter Property=""BorderBrush"" Value=""#FECACA"" />
        <Setter Property=""Foreground"" Value=""#B91C1C"" />
        <Style.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter Property=""Background"" Value=""#FEF2F2"" />
                <Setter Property=""BorderBrush"" Value=""#FCA5A5"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style x:Key=""SmallButton"" TargetType=""Button"" BasedOn=""{StaticResource SecondaryButton}"">
        <Setter Property=""FontSize"" Value=""11.5"" />
        <Setter Property=""Padding"" Value=""10,4"" />
    </Style>

    <Style x:Key=""ChipButton"" TargetType=""Button"" BasedOn=""{StaticResource BaseButton}"">
        <Setter Property=""Background"" Value=""#F1F5F9"" />
        <Setter Property=""BorderBrush"" Value=""#E2E8F0"" />
        <Setter Property=""Foreground"" Value=""#0F172A"" />
        <Setter Property=""FontFamily"" Value=""Consolas"" />
        <Setter Property=""FontWeight"" Value=""Normal"" />
        <Setter Property=""FontSize"" Value=""11.5"" />
        <Setter Property=""Padding"" Value=""8,3"" />
        <Style.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter Property=""Background"" Value=""#EFF6FF"" />
                <Setter Property=""BorderBrush"" Value=""#93C5FD"" />
                <Setter Property=""Foreground"" Value=""#1D63D5"" />
            </Trigger>
        </Style.Triggers>
    </Style>

    <Style x:Key=""ModernTextBox"" TargetType=""TextBox"">
        <Setter Property=""FontFamily"" Value=""Segoe UI"" />
        <Setter Property=""FontSize"" Value=""12.5"" />
        <Setter Property=""Foreground"" Value=""#0F172A"" />
        <Setter Property=""Background"" Value=""#FFFFFF"" />
        <Setter Property=""BorderBrush"" Value=""#CBD5E1"" />
        <Setter Property=""BorderThickness"" Value=""1"" />
        <Setter Property=""Padding"" Value=""8,5"" />
        <Setter Property=""VerticalContentAlignment"" Value=""Center"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""TextBox"">
                    <Border x:Name=""bd""
                            Background=""{TemplateBinding Background}""
                            BorderBrush=""{TemplateBinding BorderBrush}""
                            BorderThickness=""{TemplateBinding BorderThickness}""
                            CornerRadius=""4"">
                        <ScrollViewer x:Name=""PART_ContentHost"" Margin=""0"" VerticalAlignment=""Center"" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""#94A3B8"" />
                        </Trigger>
                        <Trigger Property=""IsKeyboardFocused"" Value=""True"">
                            <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""#1D63D5"" />
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter TargetName=""bd"" Property=""Background"" Value=""#F8FAFC"" />
                            <Setter Property=""Foreground"" Value=""#64748B"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""MonoTextBox"" TargetType=""TextBox"" BasedOn=""{StaticResource ModernTextBox}"">
        <Setter Property=""FontFamily"" Value=""Consolas"" />
        <Setter Property=""FontSize"" Value=""12"" />
    </Style>

    <Style x:Key=""ModernCheckBox"" TargetType=""CheckBox"">
        <Setter Property=""FontFamily"" Value=""Segoe UI"" />
        <Setter Property=""FontSize"" Value=""12.5"" />
        <Setter Property=""Foreground"" Value=""#0F172A"" />
        <Setter Property=""Cursor"" Value=""Hand"" />
        <Setter Property=""FocusVisualStyle"" Value=""{x:Null}"" />
        <Setter Property=""VerticalContentAlignment"" Value=""Center"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""CheckBox"">
                    <StackPanel Orientation=""Horizontal"" Background=""Transparent"">
                        <Border x:Name=""box""
                                Width=""16"" Height=""16""
                                Margin=""0,0,7,0""
                                CornerRadius=""3""
                                Background=""#FFFFFF""
                                BorderBrush=""#94A3B8""
                                BorderThickness=""1.2""
                                VerticalAlignment=""Center"">
                            <Path x:Name=""check""
                                  Data=""M 3,8 L 6.5,11.5 L 13,4.5""
                                  Stroke=""#FFFFFF""
                                  StrokeThickness=""1.8""
                                  StrokeStartLineCap=""Round""
                                  StrokeEndLineCap=""Round""
                                  Visibility=""Collapsed"" />
                        </Border>
                        <ContentPresenter VerticalAlignment=""Center"" />
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsChecked"" Value=""True"">
                            <Setter TargetName=""box"" Property=""Background"" Value=""#1D63D5"" />
                            <Setter TargetName=""box"" Property=""BorderBrush"" Value=""#1D63D5"" />
                            <Setter TargetName=""check"" Property=""Visibility"" Value=""Visible"" />
                        </Trigger>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""box"" Property=""BorderBrush"" Value=""#1D63D5"" />
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Opacity"" Value=""0.48"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""ModernRadioButton"" TargetType=""RadioButton"">
        <Setter Property=""FontFamily"" Value=""Segoe UI"" />
        <Setter Property=""FontSize"" Value=""12.5"" />
        <Setter Property=""Foreground"" Value=""#0F172A"" />
        <Setter Property=""Cursor"" Value=""Hand"" />
        <Setter Property=""FocusVisualStyle"" Value=""{x:Null}"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""RadioButton"">
                    <StackPanel Orientation=""Horizontal"" Background=""Transparent"">
                        <Border x:Name=""ring""
                                Width=""16"" Height=""16""
                                Margin=""0,0,7,0""
                                CornerRadius=""8""
                                Background=""#FFFFFF""
                                BorderBrush=""#94A3B8""
                                BorderThickness=""1.3""
                                VerticalAlignment=""Center"">
                            <Ellipse x:Name=""dot""
                                     Width=""8"" Height=""8""
                                     Fill=""#1D63D5""
                                     Visibility=""Collapsed"" />
                        </Border>
                        <ContentPresenter VerticalAlignment=""Center"" />
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsChecked"" Value=""True"">
                            <Setter TargetName=""ring"" Property=""BorderBrush"" Value=""#1D63D5"" />
                            <Setter TargetName=""dot"" Property=""Visibility"" Value=""Visible"" />
                        </Trigger>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""ring"" Property=""BorderBrush"" Value=""#1D63D5"" />
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Opacity"" Value=""0.48"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""ModernComboBox"" TargetType=""ComboBox"">
        <Setter Property=""FontFamily"" Value=""Segoe UI"" />
        <Setter Property=""FontSize"" Value=""12.5"" />
        <Setter Property=""Foreground"" Value=""#0F172A"" />
        <Setter Property=""Background"" Value=""#FFFFFF"" />
        <Setter Property=""BorderBrush"" Value=""#CBD5E1"" />
        <Setter Property=""BorderThickness"" Value=""1"" />
        <Setter Property=""Padding"" Value=""8,5"" />
        <Setter Property=""SnapsToDevicePixels"" Value=""True"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""ComboBox"">
                    <Grid>
                        <ToggleButton x:Name=""ToggleButton""
                                      Focusable=""False""
                                      IsChecked=""{Binding Path=IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}""
                                      ClickMode=""Press"">
                            <ToggleButton.Template>
                                <ControlTemplate TargetType=""ToggleButton"">
                                    <Border x:Name=""bd""
                                            Background=""#FFFFFF""
                                            BorderBrush=""#CBD5E1""
                                            BorderThickness=""1""
                                            CornerRadius=""4"">
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width=""*"" />
                                                <ColumnDefinition Width=""26"" />
                                            </Grid.ColumnDefinitions>
                                            <Path Grid.Column=""1""
                                                  Data=""M 0,0 L 4,4 L 8,0""
                                                  Stroke=""#64748B""
                                                  StrokeThickness=""1.5""
                                                  HorizontalAlignment=""Center""
                                                  VerticalAlignment=""Center"" />
                                        </Grid>
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property=""IsMouseOver"" Value=""True"">
                                            <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""#94A3B8"" />
                                        </Trigger>
                                        <Trigger Property=""IsChecked"" Value=""True"">
                                            <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""#1D63D5"" />
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </ToggleButton.Template>
                        </ToggleButton>
                        <ContentPresenter x:Name=""ContentSite""
                                          IsHitTestVisible=""False""
                                          Content=""{TemplateBinding SelectionBoxItem}""
                                          ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}""
                                          Margin=""10,5,28,5""
                                          VerticalAlignment=""Center""
                                          HorizontalAlignment=""Left"" />
                        <Popup x:Name=""Popup""
                               Placement=""Bottom""
                               IsOpen=""{TemplateBinding IsDropDownOpen}""
                               AllowsTransparency=""True""
                               Focusable=""False""
                               PopupAnimation=""Fade"">
                            <Border x:Name=""DropDown""
                                    MinWidth=""{TemplateBinding ActualWidth}""
                                    MaxHeight=""{TemplateBinding MaxDropDownHeight}""
                                    Margin=""0,2,0,0""
                                    Background=""#FFFFFF""
                                    BorderBrush=""#CBD5E1""
                                    BorderThickness=""1""
                                    CornerRadius=""5"">
                                <ScrollViewer SnapsToDevicePixels=""True"">
                                    <StackPanel IsItemsHost=""True"" KeyboardNavigation.DirectionalNavigation=""Contained"" />
                                </ScrollViewer>
                            </Border>
                        </Popup>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""ModernTabItem"" TargetType=""TabItem"">
        <Setter Property=""FontFamily"" Value=""Segoe UI"" />
        <Setter Property=""FontSize"" Value=""12.5"" />
        <Setter Property=""FontWeight"" Value=""SemiBold"" />
        <Setter Property=""Foreground"" Value=""#64748B"" />
        <Setter Property=""Cursor"" Value=""Hand"" />
        <Setter Property=""FocusVisualStyle"" Value=""{x:Null}"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""TabItem"">
                    <Border x:Name=""bd""
                            Background=""Transparent""
                            BorderThickness=""0,0,0,2""
                            BorderBrush=""Transparent""
                            Padding=""14,9""
                            Margin=""0,0,4,0"">
                        <ContentPresenter ContentSource=""Header"" HorizontalAlignment=""Center"" VerticalAlignment=""Center"" />
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter Property=""Foreground"" Value=""#0F172A"" />
                        </Trigger>
                        <Trigger Property=""IsSelected"" Value=""True"">
                            <Setter Property=""Foreground"" Value=""#1D63D5"" />
                            <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""#1D63D5"" />
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""ModernTabControl"" TargetType=""TabControl"">
        <Setter Property=""Background"" Value=""Transparent"" />
        <Setter Property=""BorderThickness"" Value=""0"" />
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""TabControl"">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height=""Auto"" />
                            <RowDefinition Height=""*"" />
                        </Grid.RowDefinitions>
                        <Border Grid.Row=""0""
                                Background=""#FFFFFF""
                                BorderBrush=""#E2E8F0""
                                BorderThickness=""0,0,0,1""
                                Padding=""8,0"">
                            <TabPanel IsItemsHost=""True"" />
                        </Border>
                        <ContentPresenter Grid.Row=""1"" ContentSource=""SelectedContent"" />
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key=""ModernDataGrid"" TargetType=""DataGrid"">
        <Setter Property=""Background"" Value=""#FFFFFF"" />
        <Setter Property=""BorderBrush"" Value=""#E2E8F0"" />
        <Setter Property=""BorderThickness"" Value=""1"" />
        <Setter Property=""RowBackground"" Value=""#FFFFFF"" />
        <Setter Property=""AlternatingRowBackground"" Value=""#FAFBFD"" />
        <Setter Property=""GridLinesVisibility"" Value=""Horizontal"" />
        <Setter Property=""HorizontalGridLinesBrush"" Value=""#F1F5F9"" />
        <Setter Property=""HeadersVisibility"" Value=""Column"" />
        <Setter Property=""RowHeight"" Value=""30"" />
        <Setter Property=""CanUserAddRows"" Value=""False"" />
        <Setter Property=""CanUserDeleteRows"" Value=""False"" />
        <Setter Property=""SelectionMode"" Value=""Single"" />
        <Setter Property=""AutoGenerateColumns"" Value=""False"" />
        <Setter Property=""ColumnHeaderStyle"">
            <Setter.Value>
                <Style TargetType=""DataGridColumnHeader"">
                    <Setter Property=""Background"" Value=""#F8FAFC"" />
                    <Setter Property=""Foreground"" Value=""#475569"" />
                    <Setter Property=""FontWeight"" Value=""SemiBold"" />
                    <Setter Property=""FontSize"" Value=""11.5"" />
                    <Setter Property=""Padding"" Value=""8,7"" />
                    <Setter Property=""BorderBrush"" Value=""#E2E8F0"" />
                    <Setter Property=""BorderThickness"" Value=""0,0,1,1"" />
                </Style>
            </Setter.Value>
        </Setter>
        <Setter Property=""RowStyle"">
            <Setter.Value>
                <Style TargetType=""DataGridRow"">
                    <Style.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter Property=""Background"" Value=""#F8FAFC"" />
                        </Trigger>
                        <Trigger Property=""IsSelected"" Value=""True"">
                            <Setter Property=""Background"" Value=""#EFF6FF"" />
                            <Setter Property=""Foreground"" Value=""#0F172A"" />
                        </Trigger>
                    </Style.Triggers>
                </Style>
            </Setter.Value>
        </Setter>
        <Setter Property=""CellStyle"">
            <Setter.Value>
                <Style TargetType=""DataGridCell"">
                    <Setter Property=""BorderThickness"" Value=""0"" />
                    <Setter Property=""Padding"" Value=""6,2"" />
                    <Setter Property=""Foreground"" Value=""#0F172A"" />
                    <Setter Property=""VerticalAlignment"" Value=""Center"" />
                    <Style.Triggers>
                        <Trigger Property=""IsSelected"" Value=""True"">
                            <Setter Property=""Background"" Value=""Transparent"" />
                            <Setter Property=""Foreground"" Value=""#0F172A"" />
                        </Trigger>
                    </Style.Triggers>
                </Style>
            </Setter.Value>
        </Setter>
    </Style>

</ResourceDictionary>";
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }

        // ── Component Helpers ────────────────────────────────────────────────

        public static Border MakeCard(Thickness? padding = null, Thickness? margin = null, bool subtle = false)
        {
            return new Border
            {
                Background = subtle ? SubtleSurfaceBrush : SurfaceBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = padding ?? new Thickness(14),
                Margin = margin ?? new Thickness(0, 0, 0, 12)
            };
        }

        public static StackPanel MakeSectionLabel(string title, string? subtitle = null)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimaryBrush
            });
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                sp.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11.5,
                    Foreground = TextMutedBrush,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            return sp;
        }

        public static TextBlock MakeFieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = TextSecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Border MakeStatusPill(string text, StatusTone tone)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Center
            };

            var stack = new StackPanel { Orientation = WpfOrientation.Horizontal };
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var lbl = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(dot);
            stack.Children.Add(lbl);
            pill.Child = stack;

            UpdateStatusPill(pill, text, tone);
            return pill;
        }

        public static void UpdateStatusPill(Border pill, string text, StatusTone tone)
        {
            var (bg, bd, fg) = tone switch
            {
                StatusTone.Success => (SuccessSoftBrush, SuccessBorderBrush, SuccessBrush),
                StatusTone.Warning => (WarningSoftBrush, WarningBorderBrush, WarningBrush),
                StatusTone.Danger => (DangerSoftBrush, DangerBorderBrush, DangerBrush),
                StatusTone.Info => (AccentSoftBrush, AccentBorderBrush, AccentBrush),
                _ => (SubtleSurfaceBrush, BorderBrush, TextSecondaryBrush)
            };

            pill.Background = bg;
            pill.BorderBrush = bd;

            if (pill.Child is StackPanel stack && stack.Children.Count >= 2)
            {
                if (stack.Children[0] is Ellipse dot) dot.Fill = fg;
                if (stack.Children[1] is TextBlock lbl)
                {
                    lbl.Text = text;
                    lbl.Foreground = fg;
                }
            }
        }

        public static Border MakeMetricTile(string label, out TextBlock valueText)
        {
            var tile = new Border
            {
                Background = SubtleSurfaceBrush,
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 8, 0)
            };

            var sp = new StackPanel();
            valueText = new TextBlock
            {
                Text = "0",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextPrimaryBrush
            };
            var cap = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = TextMutedBrush,
                Margin = new Thickness(0, 2, 0, 0)
            };

            sp.Children.Add(valueText);
            sp.Children.Add(cap);
            tile.Child = sp;
            return tile;
        }
    }
}
