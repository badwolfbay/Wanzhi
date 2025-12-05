using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wanzhi.Settings;
using Wanzhi.SystemIntegration;

namespace Wanzhi
{
    /// <summary>
    /// 设置窗口
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private System.Windows.Media.Color _selectedColor;
        private System.Windows.Media.Color _selectedWaveColor;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Instance;
            LoadFonts();
            LoadSettings();
        }

        private void LoadFonts()
        {
            var language = System.Windows.Markup.XmlLanguage.GetLanguage("zh-cn");
            var fontItems = new System.Collections.Generic.List<ComboBoxItem>();

            foreach (var fontFamily in Fonts.SystemFontFamilies)
            {
                string displayName;
                // 尝试获取中文名称
                if (fontFamily.FamilyNames.ContainsKey(language))
                {
                    displayName = fontFamily.FamilyNames[language];
                }
                else
                {
                    displayName = fontFamily.Source;
                }

                fontItems.Add(new ComboBoxItem 
                { 
                    Content = displayName, 
                    Tag = fontFamily.Source 
                });
            }

            // 按显示名称排序
            fontItems.Sort((a, b) => string.Compare(a.Content.ToString(), b.Content.ToString(), StringComparison.CurrentCulture));

            foreach (var item in fontItems)
            {
                FontComboBox.Items.Add(item);
            }
        }

        private void LoadSettings()
        {
            // 加载主题设置
            ThemeComboBox.SelectedIndex = _settings.Theme switch
            {
                ThemeMode.Light => 0,
                ThemeMode.Dark => 1,
                ThemeMode.System => 2,
                _ => 2
            };

            // 加载背景颜色
            _selectedColor = (System.Windows.Media.Color)ColorConverter.ConvertFromString(_settings.BackgroundColor);
            UpdateColorButtonBackground();

            // 加载波浪颜色
            _selectedWaveColor = (System.Windows.Media.Color)ColorConverter.ConvertFromString(_settings.WaveColor);
            UpdateWaveColorButtonBackground();

            // 加载动画设置
            EnableWaveCheckBox.IsChecked = _settings.EnableWaveAnimation;

            // 加载诗词设置
            // 选中对应的字体
            foreach (ComboBoxItem item in FontComboBox.Items)
            {
                if (item.Tag?.ToString() == _settings.PoetryFontFamily)
                {
                    FontComboBox.SelectedItem = item;
                    break;
                }
            }

            // 加载文字方向
            OrientationComboBox.SelectedIndex = _settings.PoetryOrientation == TextOrientation.Horizontal ? 1 : 0;

            PoetryFontSizeSlider.Value = _settings.PoetryFontSize;
            AuthorFontSizeSlider.Value = _settings.AuthorFontSize;
            RefreshIntervalSlider.Value = _settings.RefreshIntervalMinutes;

            // 加载系统设置
            AutoStartCheckBox.IsChecked = StartupManager.IsStartupEnabled();
        }

        private void UpdateColorButtonBackground()
        {
            ColorPickerButton.Background = new SolidColorBrush(_selectedColor);
            ColorPickerButton.Foreground = new SolidColorBrush(
                (_selectedColor.R * 0.299 + _selectedColor.G * 0.587 + _selectedColor.B * 0.114) > 128 
                    ? Colors.Black 
                    : Colors.White
            );
        }

        private void UpdateWaveColorButtonBackground()
        {
            WaveColorPickerButton.Background = new SolidColorBrush(_selectedWaveColor);
            WaveColorPickerButton.Foreground = new SolidColorBrush(
                (_selectedWaveColor.R * 0.299 + _selectedWaveColor.G * 0.587 + _selectedWaveColor.B * 0.114) > 128 
                    ? Colors.Black 
                    : Colors.White
            );
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 防止在初始化期间触发
            if (_settings == null) return;
            
            if (ThemeComboBox.SelectedItem is ComboBoxItem item)
            {
                _settings.Theme = item.Tag?.ToString() switch
                {
                    "Light" => ThemeMode.Light,
                    "Dark" => ThemeMode.Dark,
                    "System" => ThemeMode.System,
                    _ => ThemeMode.System
                };
            }
        }

        private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            // 简单的颜色选择器（使用预定义颜色）
            var colorDialog = new ColorSelectionDialog(_selectedColor);
            if (colorDialog.ShowDialog() == true)
            {
                _selectedColor = colorDialog.SelectedColor;
                UpdateColorButtonBackground();
                _settings.BackgroundColor = _selectedColor.ToString();
            }
        }

        private void WaveColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new ColorSelectionDialog(_selectedWaveColor);
            if (colorDialog.ShowDialog() == true)
            {
                _selectedWaveColor = colorDialog.SelectedColor;
                UpdateWaveColorButtonBackground();
                _settings.WaveColor = _selectedWaveColor.ToString();
            }
        }

        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings == null) return;
            if (FontComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _settings.PoetryFontFamily = item.Tag.ToString();
            }
        }

        private void OrientationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings == null) return;
            if (OrientationComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _settings.PoetryOrientation = item.Tag.ToString() switch
                {
                    "Horizontal" => TextOrientation.Horizontal,
                    _ => TextOrientation.Vertical
                };
            }
        }

        private void EnableWaveCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;
            _settings.EnableWaveAnimation = EnableWaveCheckBox.IsChecked ?? true;
        }

        private void PoetryFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || PoetryFontSizeText == null) return;
            var value = (int)e.NewValue;
            PoetryFontSizeText.Text = value.ToString();
            _settings.PoetryFontSize = value;
        }

        private void AuthorFontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || AuthorFontSizeText == null) return;
            var value = (int)e.NewValue;
            AuthorFontSizeText.Text = value.ToString();
            _settings.AuthorFontSize = value;
        }

        private void RefreshIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || RefreshIntervalText == null) return;
            var value = (int)e.NewValue;
            RefreshIntervalText.Text = value.ToString();
            _settings.RefreshIntervalMinutes = value;
        }

        private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var enabled = AutoStartCheckBox.IsChecked ?? false;
            StartupManager.SetStartup(enabled);
            _settings.AutoStart = enabled;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.Save();
            
            // 显示主窗口（壁纸）
            var app = Application.Current as App;
            app?.ShowMainWindow();
            
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// 简单的颜色选择对话框
    /// </summary>
    public class ColorSelectionDialog : Window
    {
        public System.Windows.Media.Color SelectedColor { get; private set; }
        private readonly List<Button> _allColorButtons = new List<Button>();

        public ColorSelectionDialog(System.Windows.Media.Color currentColor)
        {
            Title = "选择颜色";
            Width = 420;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            SelectedColor = currentColor;

            var mainPanel = new StackPanel { Margin = new Thickness(10) };
            
            mainPanel.Children.Add(new TextBlock { Text = "浅色 / 背景色", Margin = new Thickness(0,0,0,5), FontWeight = FontWeights.Bold });
            
            var lightColorsPanel = new WrapPanel();
            var lightColors = new[]
            {
                "#FFF5F5F5", "#FFFFFFFF", "#FFF0F9FF", "#FFF0FDF4",
                "#FFFFF7ED", "#FFFDF2F8", "#FFFAFAFA", "#FFE0E0E0",
                "#FFD1D5DB", "#FFBFDBFE", "#FFBBF7D0", "#FFFDE68A"
            };
            AddColorButtons(lightColorsPanel, lightColors, currentColor);
            mainPanel.Children.Add(lightColorsPanel);

            mainPanel.Children.Add(new TextBlock { Text = "深色 / 波浪色", Margin = new Thickness(0,10,0,5), FontWeight = FontWeights.Bold });

            var darkColorsPanel = new WrapPanel();
            var darkColors = new[]
            {
                "#FF1E3A8A", "#FF7C3AED", "#FFDC2626", "#FFEA580C",
                "#FFD97706", "#FF65A30D", "#FF059669", "#FF0891B2",
                "#FF0284C7", "#FF4F46E5", "#FF9333EA", "#FFDB2777",
                "#FF334155", "#FF475569", "#FF64748B", "#FF94A3B8"
            };
            AddColorButtons(darkColorsPanel, darkColors, currentColor);
            mainPanel.Children.Add(darkColorsPanel);

            Content = mainPanel;
        }

        private void AddColorButtons(Panel panel, string[] colors, System.Windows.Media.Color currentColor)
        {
            foreach (var colorStr in colors)
            {
                var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(colorStr);
                var button = new Button
                {
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(3),
                    Background = new SolidColorBrush(color),
                    BorderThickness = new Thickness(2),
                    BorderBrush = color == currentColor ? Brushes.Black : Brushes.LightGray,
                    Tag = color // Store color in Tag for later reference
                };

                button.Click += (s, e) =>
                {
                    // Clear all button borders first
                    foreach (var btn in _allColorButtons)
                    {
                        btn.BorderBrush = Brushes.LightGray;
                    }
                    
                    // Highlight the clicked button
                    button.BorderBrush = Brushes.Black;
                    
                    SelectedColor = color;
                    DialogResult = true;
                    Close();
                };

                _allColorButtons.Add(button);
                panel.Children.Add(button);
            }
        }
    }
}
