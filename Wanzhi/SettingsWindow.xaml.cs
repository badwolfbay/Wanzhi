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
        
        /// <summary>
        /// 当为 true 时真正关闭窗口，否则只是隐藏
        /// </summary>
        public bool CanClose { get; set; } = false;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Instance;
            LoadFonts();
            LoadFonts();
            RefreshState();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!CanClose)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
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
                AuthorFontComboBox.Items.Add(new ComboBoxItem
                {
                    Content = item.Content,
                    Tag = item.Tag
                });
            }
        }

        public void RefreshState()
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

            // 加载诗词设置
            // 选中对应的正文字体
            foreach (ComboBoxItem item in FontComboBox.Items)
            {
                if (item.Tag?.ToString() == _settings.PoetryFontFamily)
                {
                    FontComboBox.SelectedItem = item;
                    break;
                }
            }

            // 选中对应的标题/作者字体
            foreach (ComboBoxItem item in AuthorFontComboBox.Items)
            {
                if (item.Tag?.ToString() == _settings.AuthorFontFamily)
                {
                    AuthorFontComboBox.SelectedItem = item;
                    break;
                }
            }

            // 加载文字方向
            OrientationComboBox.SelectedIndex = _settings.PoetryOrientation == TextOrientation.Horizontal ? 1 : 0;

            // 加载竖排对齐
            VerticalAlignComboBox.SelectedIndex = _settings.VerticalPoetryAlignment switch
            {
                VerticalTextAlignment.Top => 0,
                VerticalTextAlignment.Center => 1,
                VerticalTextAlignment.Bottom => 2,
                _ => 1
            };

            // 加载横排对齐
            HorizontalAlignComboBox.SelectedIndex = _settings.HorizontalPoetryAlignment switch
            {
                HorizontalTextAlignment.Left => 0,
                HorizontalTextAlignment.Center => 1,
                HorizontalTextAlignment.Right => 2,
                _ => 1
            };

            PoetryFontSizeSlider.Value = _settings.PoetryFontSize;
            AuthorFontSizeSlider.Value = _settings.AuthorFontSize;
            RefreshIntervalSlider.Value = _settings.RefreshIntervalMinutes;

            // 加载字符间距设置
            PoetryCharacterSpacingSlider.Value = _settings.PoetryCharacterSpacing;
            PoetryVerticalCharacterSpacingSlider.Value = _settings.PoetryVerticalCharacterSpacing;

            // 加载落款偏移
            VerticalPoetryOffsetSlider.Value = _settings.VerticalPoetryOffset;
            VerticalPoetryOffsetText.Text = _settings.VerticalPoetryOffset.ToString();
            HorizontalPoetryOffsetSlider.Value = _settings.HorizontalPoetryOffset;
            HorizontalPoetryOffsetText.Text = _settings.HorizontalPoetryOffset.ToString();

            // 根据当前文字方向更新字符间距控件可见性
            UpdateCharacterSpacingControlsVisibility();

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

        private void AuthorFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings == null) return;
            if (AuthorFontComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _settings.AuthorFontFamily = item.Tag.ToString();
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
                
                // 更新字符间距控件的可见性
                UpdateCharacterSpacingControlsVisibility();
            }
        }

        private void VerticalAlignComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings == null) return;
            if (VerticalAlignComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _settings.VerticalPoetryAlignment = item.Tag.ToString() switch
                {
                    "Top" => VerticalTextAlignment.Top,
                    "Bottom" => VerticalTextAlignment.Bottom,
                    _ => VerticalTextAlignment.Center
                };
            }
        }

        private void HorizontalAlignComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings == null) return;
            if (HorizontalAlignComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _settings.HorizontalPoetryAlignment = item.Tag.ToString() switch
                {
                    "Left" => HorizontalTextAlignment.Left,
                    "Right" => HorizontalTextAlignment.Right,
                    _ => HorizontalTextAlignment.Center
                };
            }
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

        private void PoetryCharacterSpacingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || PoetryCharacterSpacingText == null) return;
            var value = (int)e.NewValue;
            PoetryCharacterSpacingText.Text = value.ToString();
            _settings.PoetryCharacterSpacing = value;
        }

        private void PoetryVerticalCharacterSpacingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || PoetryVerticalCharacterSpacingText == null) return;
            var value = (int)e.NewValue;
            PoetryVerticalCharacterSpacingText.Text = value.ToString();
            _settings.PoetryVerticalCharacterSpacing = value;
        }

        private void RefreshIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || RefreshIntervalText == null) return;
            var value = (int)e.NewValue;
            RefreshIntervalText.Text = value.ToString();
            _settings.RefreshIntervalMinutes = value;
        }

        private void VerticalPoetryOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || VerticalPoetryOffsetText == null) return;
            var value = (int)e.NewValue;
            VerticalPoetryOffsetText.Text = value.ToString();
            _settings.VerticalPoetryOffset = value;
        }

        private void HorizontalPoetryOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings == null || HorizontalPoetryOffsetText == null) return;
            var value = (int)e.NewValue;
            HorizontalPoetryOffsetText.Text = value.ToString();
            _settings.HorizontalPoetryOffset = value;
        }

        private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var enabled = AutoStartCheckBox.IsChecked ?? false;
            StartupManager.SetStartup(enabled);
            _settings.AutoStart = enabled;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.Save();
            
            // 显示主窗口（壁纸）
            var app = Application.Current as App;
            if (app != null)
            {
                await app.ShowMainWindowAsync();
            }
            
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 根据文字方向更新字符间距控件的可见性
        /// </summary>
        private void UpdateCharacterSpacingControlsVisibility()
        {
            if (OrientationComboBox == null || 
                HorizontalCharacterSpacingPanel == null || 
                VerticalCharacterSpacingPanel == null)
                return;

            // 根据当前选择的文字方向显示相应的字符间距设置
            bool isHorizontal = OrientationComboBox.SelectedIndex == 1; // 横排
            if (isHorizontal)
            {
                HorizontalCharacterSpacingPanel.Visibility = Visibility.Visible;
                VerticalCharacterSpacingPanel.Visibility = Visibility.Collapsed;
            }
            else // 竖排
            {
                HorizontalCharacterSpacingPanel.Visibility = Visibility.Collapsed;
                VerticalCharacterSpacingPanel.Visibility = Visibility.Visible;
            }

            // 根据当前文字方向显示对应的对齐方式设置（隐藏不适用的选项）
            if (VerticalAlignLabel != null) VerticalAlignLabel.Visibility = isHorizontal ? Visibility.Collapsed : Visibility.Visible;
            if (VerticalAlignComboBox != null) VerticalAlignComboBox.Visibility = isHorizontal ? Visibility.Collapsed : Visibility.Visible;
            if (HorizontalAlignLabel != null) HorizontalAlignLabel.Visibility = isHorizontal ? Visibility.Visible : Visibility.Collapsed;
            if (HorizontalAlignComboBox != null) HorizontalAlignComboBox.Visibility = isHorizontal ? Visibility.Visible : Visibility.Collapsed;

            // 同步更新落款偏移量控件的可见性
            if (HorizontalPoetryOffsetLabel != null) HorizontalPoetryOffsetLabel.Visibility = isHorizontal ? Visibility.Visible : Visibility.Collapsed;
            if (HorizontalPoetryOffsetSlider != null) HorizontalPoetryOffsetSlider.Visibility = isHorizontal ? Visibility.Visible : Visibility.Collapsed;
            if (HorizontalPoetryOffsetText != null) HorizontalPoetryOffsetText.Visibility = isHorizontal ? Visibility.Visible : Visibility.Collapsed;

            if (VerticalPoetryOffsetLabel != null) VerticalPoetryOffsetLabel.Visibility = isHorizontal ? Visibility.Collapsed : Visibility.Visible;
            if (VerticalPoetryOffsetSlider != null) VerticalPoetryOffsetSlider.Visibility = isHorizontal ? Visibility.Collapsed : Visibility.Visible;
            if (VerticalPoetryOffsetText != null) VerticalPoetryOffsetText.Visibility = isHorizontal ? Visibility.Collapsed : Visibility.Visible;
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
            Height = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            SelectedColor = currentColor;

            var mainPanel = new StackPanel { Margin = new Thickness(10) };
            Content = mainPanel;
            
            // 显示加载提示
            var loadingText = new TextBlock 
            { 
                Text = "加载中...", 
                Margin = new Thickness(10),
                FontSize = 14,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            mainPanel.Children.Add(loadingText);
            
            // 异步加载UI元素，避免阻塞
            Dispatcher.BeginInvoke(new Action(() =>
            {
                mainPanel.Children.Remove(loadingText);
                BuildColorPickers(mainPanel, currentColor);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void BuildColorPickers(StackPanel mainPanel, System.Windows.Media.Color currentColor)
        {
            // 浅色区域
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

            // 深色区域
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

            // 自定义颜色输入区域
            mainPanel.Children.Add(new TextBlock { Text = "自定义颜色", Margin = new Thickness(0,15,0,5), FontWeight = FontWeights.Bold });
            
            var customColorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,10) };
            
            // 输入框
            var customColorTextBox = new TextBox 
            { 
                Width = 120, 
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5),
                Text = currentColor.ToString()
            };
            
            // 预览框
            var previewBorder = new Border
            {
                Width = 40,
                Height = 30,
                Margin = new Thickness(10,0,10,0),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(currentColor)
            };
            
            // 确定按钮
            var applyButton = new Button
            {
                Content = "应用",
                Width = 60,
                Height = 30,
                Padding = new Thickness(5)
            };
            
            // 错误提示
            var errorText = new TextBlock
            {
                Foreground = Brushes.Red,
                Margin = new Thickness(10,0,0,0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            
            // 输入框文本变化事件 - 实时预览
            customColorTextBox.TextChanged += (s, e) =>
            {
                try
                {
                    var inputText = customColorTextBox.Text.Trim();
                    // 如果不以#开头，自动添加
                    if (!inputText.StartsWith("#"))
                    {
                        inputText = "#" + inputText;
                    }
                    
                    var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(inputText);
                    previewBorder.Background = new SolidColorBrush(color);
                    errorText.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    errorText.Text = "无效颜色";
                    errorText.Visibility = Visibility.Visible;
                }
            };
            
            // 应用按钮点击事件
            applyButton.Click += (s, e) =>
            {
                try
                {
                    var inputText = customColorTextBox.Text.Trim();
                    if (!inputText.StartsWith("#"))
                    {
                        inputText = "#" + inputText;
                    }
                    
                    var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(inputText);
                    SelectedColor = color;
                    DialogResult = true;
                    Close();
                }
                catch
                {
                    errorText.Text = "无效颜色代码";
                    errorText.Visibility = Visibility.Visible;
                }
            };
            
            customColorPanel.Children.Add(new TextBlock { Text = "#", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,5,0) });
            customColorPanel.Children.Add(customColorTextBox);
            customColorPanel.Children.Add(previewBorder);
            customColorPanel.Children.Add(applyButton);
            customColorPanel.Children.Add(errorText);
            
            mainPanel.Children.Add(customColorPanel);
            
            // 提示文本
            mainPanel.Children.Add(new TextBlock 
            { 
                Text = "提示: 输入6位或8位十六进制颜色代码，如 FF5733 或 FFFF5733", 
                FontSize = 10,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            });
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
