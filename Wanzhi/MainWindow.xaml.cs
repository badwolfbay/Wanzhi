using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wanzhi.Models;
using Wanzhi.Rendering;
using Wanzhi.Services;
using Wanzhi.Settings;
using Wanzhi.SystemIntegration;

namespace Wanzhi;

/// <summary>
/// 主窗口 - 壁纸显示
/// </summary>
public partial class MainWindow : Window
{
    private readonly JinrishiciService _poetryService;
    private WaveRenderer? _waveRenderer;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _poetryRefreshTimer;
    private readonly ThemeDetector _themeDetector;
    private DateTime _lastAnimationTime;
    private bool _isDarkTheme;

    // Windows API for setting wallpaper
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    public MainWindow()
    {
        InitializeComponent();

        _poetryService = new JinrishiciService();
        _themeDetector = new ThemeDetector();
        _lastAnimationTime = DateTime.Now;

        // 初始化动画定时器
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
        };
        _animationTimer.Tick += AnimationTimer_Tick;

        // 初始化诗词刷新定时器
        _poetryRefreshTimer = new DispatcherTimer();
        _poetryRefreshTimer.Tick += async (s, e) => 
        {
            await LoadPoetryAsync();
            // 如果是在后台运行且当前为静态壁纸模式，自动更新壁纸
            if (Visibility != Visibility.Visible && AppSettings.Instance.WallpaperMode == WallpaperMode.Static)
            {
                await ApplyAsWallpaperAsync(silent: true);
            }
        };

        // 监听设置变化
        AppSettings.Instance.PropertyChanged += Settings_PropertyChanged;

        // 手动初始化应用逻辑
        _ = InitializeApp();
    }

    private async System.Threading.Tasks.Task InitializeApp()
    {
        App.Log("Initializing App...");
        
        // 设置窗口覆盖所有显示器
        SetupWindowBounds();

        // 应用当前主题
        ApplyTheme();

        // 初始化波浪渲染器
        if (AppSettings.Instance.EnableWaveAnimation)
        {
            InitializeWaveRenderer();
        }

        // 加载诗词（后台异步，不阻塞启动）
        _ = LoadPoetryAsync();

        // 启动动画
        if (AppSettings.Instance.EnableWaveAnimation)
        {
            _animationTimer.Start();
        }

        // 设置诗词刷新间隔
        UpdateRefreshInterval();
        
        // 隐藏窗口，等待用户主动应用壁纸
        Hide();
        
        App.Log("App Initialized.");
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 逻辑已移至 InitializeApp
        Hide();
    }

    private void SetupWindowBounds()
    {
        // 获取主屏幕尺寸（WPF方法）
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        
        // 设置窗口大小以匹配屏幕，用于渲染
        Width = screenWidth;
        Height = screenHeight;
        
        // 确保 ViewBox 或 Grid 填满
        if (Content is FrameworkElement content)
        {
            content.Width = screenWidth;
            content.Height = screenHeight;
        }

        App.Log($"设置窗口尺寸: {Width}x{Height}");
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _animationTimer.Stop();
        _poetryRefreshTimer.Stop();
    }

    private void InitializeWaveRenderer()
    {
        if (_waveRenderer != null) return;

        var width = SystemParameters.PrimaryScreenWidth;
        // WaveCanvas 在 XAML 中高度固定为 400，且 VerticalAlignment="Bottom"
        // 所以渲染器的高度必须匹配 Canvas 的高度，否则波浪会被画在 Canvas 之外
        var height = 400.0;

        _waveRenderer = new WaveRenderer(width, height, 5); // 5层波浪
        
        // 添加波浪路径到画布
        foreach (var path in _waveRenderer.GetWavePaths())
        {
            WaveCanvas.Children.Add(path);
        }

        // 初始应用颜色
        ApplyTheme();
        
        // 立即更新一次以生成初始形状，否则第一帧可能是空的
        _waveRenderer.Update(0);
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (_waveRenderer == null) return;

        var now = DateTime.Now;
        var deltaTime = (now - _lastAnimationTime).TotalSeconds;
        _lastAnimationTime = now;

        _waveRenderer.Update(deltaTime);
    }

    private PoetryData? _currentPoetry;

    private async System.Threading.Tasks.Task LoadPoetryAsync()
    {
        PoetryData? poetry = null;
        try
        {
            App.Log("开始加载诗词...");
            // Add a timeout to prevent hanging indefinitely
            var timeoutTask = System.Threading.Tasks.Task.Delay(10000);
            var loadTask = _poetryService.GetPoetryAsync();
            
            var completedTask = await System.Threading.Tasks.Task.WhenAny(loadTask, timeoutTask);
            if (completedTask == loadTask)
            {
                poetry = await loadTask;
            }
            else
            {
                App.Log("加载诗词超时");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载诗词失败: {ex.Message}");
            App.Log($"加载诗词异常: {ex.Message}");
        }

        if (poetry == null)
        {
            App.Log("获取失败或为空，使用兜底诗词");
            poetry = new PoetryData
            {
                Content = "如果不去遍历世界，我们就不知道什么是我们精神和情感的寄托，但我们一旦遍历了世界，却发现我们可以承担的，往往只有自己的心灵。",
                Origin = new OriginInfo
                {
                    Title = "旅行",
                    Author = "测试",
                    Dynasty = "现代"
                }
            };
        }

        if (poetry != null)
        {
            App.Log($"诗词加载成功: {poetry.Content.Substring(0, Math.Min(5, poetry.Content.Length))}...");
            _currentPoetry = poetry;
            UpdatePoetryDisplay(poetry);

            // 静态模式下才自动保存一次壁纸
            if (AppSettings.Instance.WallpaperMode == WallpaperMode.Static)
            {
                _ = ApplyAsWallpaperAsync(silent: true);
            }
        }
    }

    private Color GetLegibleTextColor(Color backgroundColor)
    {
        // Calculate relative luminance
        // RGB values are 0-255, so we normalize effectively by not dividing, 
        // using the threshold of 128 instead of 0.5
        double luminance = (0.299 * backgroundColor.R + 0.587 * backgroundColor.G + 0.114 * backgroundColor.B);
        
        var color = luminance < 128 ? Colors.White : Color.FromRgb(55, 71, 79);
        App.Log($"背景亮度: {luminance}, 文本颜色: {color}");
        return color;
    }

    private void UpdatePoetryDisplay(PoetryData poetry)
    {
        Dispatcher.Invoke(() =>
        {
            PoetryContainer.Children.Clear();
            App.Log("正在更新诗词 UI...");

            // Store current poetry for re-rendering on theme change
            _currentPoetry = poetry;

            // Determine text color based on background
            var settings = AppSettings.Instance;
            Color bgColor;
            try
            {
                bgColor = (Color)ColorConverter.ConvertFromString(settings.BackgroundColor);
            }
            catch
            {
                bgColor = Color.FromRgb(245, 245, 245);
            }

            var textColorVal = GetLegibleTextColor(bgColor);
            var textColor = new SolidColorBrush(textColorVal);
            var secondaryTextColor = new SolidColorBrush(textColorVal == Colors.White ? Color.FromRgb(200, 200, 200) : Brushes.Gray.Color);

            App.Log($"应用文本颜色: {textColorVal}");

            // 根据设置选择渲染模式
            if (AppSettings.Instance.PoetryOrientation == Settings.TextOrientation.Horizontal)
            {
                RenderHorizontalPoetry(poetry, textColor, secondaryTextColor);
            }
            else
            {
                RenderVerticalPoetry(poetry, textColor, secondaryTextColor);
            }
        });
    }

    private void RenderVerticalPoetry(PoetryData poetry, SolidColorBrush textColor, SolidColorBrush secondaryTextColor)
    {
        // 设置容器为水平排列（每一竖行从右向左）
        PoetryContainer.Orientation = Orientation.Horizontal;
        PoetryContainer.FlowDirection = FlowDirection.RightToLeft;

        // 根据设置控制竖排对齐方式（正文列与落款列统一对齐）
        VerticalAlignment columnAlignment;
        switch (AppSettings.Instance.VerticalPoetryAlignment)
        {
            case VerticalTextAlignment.Top:
                columnAlignment = VerticalAlignment.Top;
                break;
            case VerticalTextAlignment.Bottom:
                columnAlignment = VerticalAlignment.Bottom;
                break;
            default:
                columnAlignment = VerticalAlignment.Center;
                break;
        }

        PoetryContainer.VerticalAlignment = columnAlignment;

        // 1. 处理正文 (从右向左排列，第一句在最右)
        var sentences = poetry.Content.Split(new[] { '，', '。', '？', '！', '；', ',', '.', '?', '!', ';' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var sentence in sentences)
        {
            var column = new StackPanel 
            { 
                Orientation = Orientation.Vertical, 
                Margin = new Thickness(15, 0, 15, 0),
                VerticalAlignment = columnAlignment
            };

            foreach (var c in sentence)
            {
                // 在竖排模式下不显示顿号"、"以获得更好的视觉效果
                if (c == '、')
                {
                    continue;
                }

                column.Children.Add(new TextBlock 
                { 
                    Text = c.ToString(), 
                    FontSize = AppSettings.Instance.PoetryFontSize, 
                    FontFamily = new FontFamily(AppSettings.Instance.PoetryFontFamily), // 使用设置的字体
                    Foreground = textColor, 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, AppSettings.Instance.PoetryVerticalCharacterSpacing, 0, AppSettings.Instance.PoetryVerticalCharacterSpacing)
                });
            }
            
            PoetryContainer.Children.Add(column);
        }

        // 2. 处理落款 (在最左边，即最后添加)
        var footerColumn = new StackPanel 
        { 
            Orientation = Orientation.Vertical, 
            Margin = new Thickness(40, 0, 0, 0),
            VerticalAlignment = columnAlignment 
        };
        footerColumn.RenderTransform = new TranslateTransform(0, AppSettings.Instance.VerticalPoetryOffset);

        // 标题 「标题」 -> ﹁标题﹂
        if (!string.IsNullOrEmpty(poetry.Origin?.Title))
        {
            var titleStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 15) };
            
            // 上引号 (竖排专用)
            titleStack.Children.Add(new TextBlock { 
                Text = "﹁", 
                FontSize = 16, 
                FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily),
                Foreground = secondaryTextColor,
                HorizontalAlignment = HorizontalAlignment.Center, 
                Margin = new Thickness(0, 0, 0, 5) 
            });
            
            foreach (var c in poetry.Origin.Title)
            {
                titleStack.Children.Add(new TextBlock 
                { 
                    Text = c.ToString(), 
                    FontSize = AppSettings.Instance.AuthorFontSize, // 使用作者字体大小
                    FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily),
                    Foreground = secondaryTextColor,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 1) // 固定美观间距
                });
            }
            
            // 下引号 (竖排专用)
            titleStack.Children.Add(new TextBlock { 
                Text = "﹂", 
                FontSize = AppSettings.Instance.AuthorFontSize, // 使用作者字体大小
                FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily),
                Foreground = secondaryTextColor,
                HorizontalAlignment = HorizontalAlignment.Center, 
                Margin = new Thickness(0, 5, 0, 0) 
            });
            
            footerColumn.Children.Add(titleStack);
        }

        // 作者印章
        if (!string.IsNullOrEmpty(poetry.Origin?.Author))
        {
            var sealBorder = new Border 
            { 
                Background = new SolidColorBrush(Color.FromRgb(183, 28, 28)), // #B71C1C
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 8, 4, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            var sealStack = new StackPanel { Orientation = Orientation.Vertical };
            
            foreach (var c in poetry.Origin.Author)
            {
                sealStack.Children.Add(new TextBlock 
                { 
                    Text = c.ToString(), 
                    FontSize = Math.Max(10, AppSettings.Instance.AuthorFontSize * 0.8), // 印章字体稍小
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily),
                    Foreground = Brushes.White, 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 1) // 固定美观间距
                });
            }
            
            sealBorder.Child = sealStack;
            footerColumn.Children.Add(sealBorder);
        }
        
        PoetryContainer.Children.Add(footerColumn);
    }

    private void RenderHorizontalPoetry(PoetryData poetry, SolidColorBrush textColor, SolidColorBrush secondaryTextColor)
    {
        // 设置容器为垂直排列（每一行从上向下），从左向右阅读
        PoetryContainer.Orientation = Orientation.Vertical;
        PoetryContainer.FlowDirection = FlowDirection.LeftToRight;
        PoetryContainer.VerticalAlignment = VerticalAlignment.Center;
        
        // 1. 处理正文
        var sentences = poetry.Content.Split(new[] { '，', '。', '？', '！', '；', ',', '.', '?', '!', ';' }, StringSplitOptions.RemoveEmptyEntries);
        
        var contentStack = new StackPanel 
        { 
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 40)
        };

        // 根据设置控制横排正文的左右对齐
        switch (AppSettings.Instance.HorizontalPoetryAlignment)
        {
            case HorizontalTextAlignment.Left:
                contentStack.HorizontalAlignment = HorizontalAlignment.Left;
                break;
            case HorizontalTextAlignment.Right:
                contentStack.HorizontalAlignment = HorizontalAlignment.Right;
                break;
            default:
                contentStack.HorizontalAlignment = HorizontalAlignment.Center;
                break;
        }

        foreach (var sentence in sentences)
        {
            // 创建一个容器来放置带间距的字符
            var sentencePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, AppSettings.Instance.PoetryLineSpacing)
            };

            // 逐个添加字符并应用字符间距
            for (int i = 0; i < sentence.Length; i++)
            {
                var charTextBlock = new TextBlock
                {
                    Text = sentence[i].ToString(),
                    FontSize = AppSettings.Instance.PoetryFontSize,
                    FontFamily = new FontFamily(AppSettings.Instance.PoetryFontFamily),
                    Foreground = textColor,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 除了最后一个字符，其他字符右边都加上间距
                if (i < sentence.Length - 1)
                {
                    charTextBlock.Margin = new Thickness(0, 0, AppSettings.Instance.PoetryCharacterSpacing, 0);
                }

                sentencePanel.Children.Add(charTextBlock);
            }

            contentStack.Children.Add(sentencePanel);
        }
        PoetryContainer.Children.Add(contentStack);

        // 2. 处理落款 (标题和作者) - 横排时同一行显示
        var footerStack = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0) 
        };
        footerStack.RenderTransform = new TranslateTransform(AppSettings.Instance.HorizontalPoetryOffset, 0);

        // 根据设置控制横排落款的左右对齐
        switch (AppSettings.Instance.HorizontalPoetryAlignment)
        {
            case HorizontalTextAlignment.Left:
                footerStack.HorizontalAlignment = HorizontalAlignment.Left;
                break;
            case HorizontalTextAlignment.Right:
                footerStack.HorizontalAlignment = HorizontalAlignment.Right;
                break;
            default:
                footerStack.HorizontalAlignment = HorizontalAlignment.Center;
                break;
        }

        // 标题
        if (!string.IsNullOrEmpty(poetry.Origin?.Title))
        {
            // 创建一个容器来放置带间距的字符
            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0)
            };

            // 逐个添加字符并应用字符间距
            var title = $"「{poetry.Origin.Title}」";
            for (int i = 0; i < title.Length; i++)
            {
                var charTextBlock = new TextBlock
                {
                    Text = title[i].ToString(),
                    FontSize = AppSettings.Instance.AuthorFontSize * 1.2, // 标题稍大
                    FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily),
                    Foreground = secondaryTextColor,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 除了最后一个字符，其他字符右边都加上固定间距
                if (i < title.Length - 1)
                {
                    charTextBlock.Margin = new Thickness(0, 0, 1, 0); // 固定美观间距
                }

                titlePanel.Children.Add(charTextBlock);
            }

            footerStack.Children.Add(titlePanel);
        }

        // 作者 (带红色印章背景)
        if (!string.IsNullOrEmpty(poetry.Origin?.Author))
        {
            var sealBorder = new Border 
            { 
                Background = new SolidColorBrush(Color.FromRgb(183, 28, 28)), // #B71C1C
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(8, 4, 8, 4), // 横排稍微宽一点
                VerticalAlignment = VerticalAlignment.Center
            };
            
            // 创建一个容器来放置带间距的字符
            var authorPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 逐个添加字符并应用字符间距
            var author = poetry.Origin.Author;
            for (int i = 0; i < author.Length; i++)
            {
                var charTextBlock = new TextBlock
                {
                    Text = author[i].ToString(),
                    FontSize = AppSettings.Instance.AuthorFontSize,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily),
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 除了最后一个字符，其他字符右边都加上固定间距
                if (i < author.Length - 1)
                {
                    charTextBlock.Margin = new Thickness(0, 0, 1, 0); // 固定美观间距
                }

                authorPanel.Children.Add(charTextBlock);
            }

            sealBorder.Child = authorPanel;
            footerStack.Children.Add(sealBorder);
        }
        
        PoetryContainer.Children.Add(footerStack);
    }

    private void ApplyTheme()
    {
        var settings = AppSettings.Instance;
        Color bgColor;
        
        // 根据主题模式确定背景颜色
        switch (settings.Theme)
        {
            case ThemeMode.Light:
                bgColor = Color.FromRgb(245, 245, 245); // 浅灰色背景
                break;
                
            case ThemeMode.Dark:
                bgColor = Color.FromRgb(30, 30, 30); // 深色背景
                break;
                
            case ThemeMode.System:
                // 检测系统主题
                bool isSystemDark = _themeDetector.IsDarkTheme();
                bgColor = isSystemDark 
                    ? Color.FromRgb(30, 30, 30)  // 深色
                    : Color.FromRgb(245, 245, 245); // 浅色
                break;
                
            default:
                bgColor = Color.FromRgb(245, 245, 245);
                break;
        }
        
        // 如果用户自定义了背景色（不是默认的深蓝色），使用自定义颜色覆盖主题默认值
        try
        {
            // 默认背景色是 #FF1E3A8A (深蓝色)
            if (settings.BackgroundColor != "#FF1E3A8A")
            {
                var customColor = (Color)ColorConverter.ConvertFromString(settings.BackgroundColor);
                bgColor = customColor;
            }
        }
        catch
        {
            // 如果解析失败，使用主题默认颜色
        }
        
        BackgroundRect.Fill = new SolidColorBrush(bgColor);

        // 确定当前主题 (基于背景亮度)
        // 计算相对亮度: 0.299*R + 0.587*G + 0.114*B
        double luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B);
        _isDarkTheme = luminance < 128; // 亮度低则是深色主题
        
        // 应用波浪颜色
        try
        {
            var waveBaseColor = (Color)ColorConverter.ConvertFromString(settings.WaveColor);
            if (_waveRenderer != null)
            {
                _waveRenderer.UpdateColor(waveBaseColor, _isDarkTheme);
            }
        }
        catch
        {
            // 默认深蓝色
            var defaultWaveColor = Color.FromRgb(57, 73, 171);
            if (_waveRenderer != null)
            {
                _waveRenderer.UpdateColor(defaultWaveColor, _isDarkTheme);
            }
        }
    }

    private void UpdateRefreshInterval()
    {
        var interval = AppSettings.Instance.RefreshIntervalMinutes;
        _poetryRefreshTimer.Interval = TimeSpan.FromMinutes(interval);
        _poetryRefreshTimer.Start();
    }

    private async void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.WallpaperMode):
                // 壁纸模式变化时，重新应用壁纸
                var settings = AppSettings.Instance;
                if (settings.WallpaperMode == WallpaperMode.Dynamic)
                {
                    // 动态壁纸模式
                    if (Visibility != Visibility.Visible)
                    {
                        Show();
                    }
                    
                    // 尝试将窗口嵌入桌面
                    bool attached = DesktopHost.Attach(this);
                    if (!attached)
                    {
                        // 如果嵌入失败，回退到静态模式
                        settings.WallpaperMode = WallpaperMode.Static;
                        await ApplyAsWallpaperAsync(silent: true);
                    }
                    else
                    {
                        // 确保动画开启（如果启用）
                        if (settings.EnableWaveAnimation && _waveRenderer != null)
                        {
                            _animationTimer.Start();
                        }
                    }
                }
                else
                {
                    // 静态壁纸模式
                    // 从桌面宿主中移除窗口
                    DesktopHost.Detach(this);
                    
                    // 隐藏窗口
                    Hide();
                    
                    // 应用为静态壁纸
                    await ApplyAsWallpaperAsync(silent: true);
                    
                    // 停止动画以节省资源
                    _animationTimer.Stop();
                }
                break;

            case nameof(AppSettings.Theme):
            case nameof(AppSettings.BackgroundColor):
                ApplyTheme();
                // 如果当前是静态壁纸模式，更新壁纸
                if (_currentPoetry != null && AppSettings.Instance.WallpaperMode == WallpaperMode.Static)
                {
                    UpdatePoetryDisplay(_currentPoetry);
                    await ApplyAsWallpaperAsync(silent: true);
                }
                break;

            case nameof(AppSettings.WaveColor):
                ApplyTheme();
                // 如果当前是动态壁纸模式，直接更新显示
                if (AppSettings.Instance.WallpaperMode == WallpaperMode.Dynamic && _currentPoetry != null)
                {
                    UpdatePoetryDisplay(_currentPoetry);
                }
                break;
            
            case nameof(AppSettings.EnableWaveAnimation):
                if (AppSettings.Instance.EnableWaveAnimation)
                {
                    InitializeWaveRenderer();
                    // 只有在动态壁纸模式下才启动动画
                    if (AppSettings.Instance.WallpaperMode == WallpaperMode.Dynamic)
                    {
                        _animationTimer.Start();
                    }
                }
                else
                {
                    // 仅停止动画计时器，保留当前波浪形状作为静态背景
                    _animationTimer.Stop();
                }
                break;

            case nameof(AppSettings.RefreshIntervalMinutes):
                UpdateRefreshInterval();
                break;

            case nameof(AppSettings.PoetryFontSize):
            case nameof(AppSettings.PoetryFontFamily):
            case nameof(AppSettings.AuthorFontSize):
            case nameof(AppSettings.AuthorFontFamily):
            case nameof(AppSettings.PoetryOrientation):
            case nameof(AppSettings.VerticalPoetryAlignment):
            case nameof(AppSettings.HorizontalPoetryAlignment):
                // 重新加载诗词以应用新样式
                if (_currentPoetry != null) 
                {
                    UpdatePoetryDisplay(_currentPoetry);
                    // 如果是静态壁纸模式，更新壁纸
                    if (AppSettings.Instance.WallpaperMode == WallpaperMode.Static)
                    {
                        await ApplyAsWallpaperAsync(silent: true);
                    }
                }
                else
                {
                    _ = LoadPoetryAsync();
                }
                break;
                
            case nameof(AppSettings.PoetryCharacterSpacing):
            case nameof(AppSettings.PoetryVerticalCharacterSpacing):
            case nameof(AppSettings.VerticalPoetryOffset):
            case nameof(AppSettings.HorizontalPoetryOffset):
                // 重新加载诗词以应用新样式
                if (_currentPoetry != null) 
                {
                    UpdatePoetryDisplay(_currentPoetry);
                    // 如果是静态壁纸模式，更新壁纸
                    if (AppSettings.Instance.WallpaperMode == WallpaperMode.Static)
                    {
                        await ApplyAsWallpaperAsync(silent: true);
                    }
                }
                else
                {
                    _ = LoadPoetryAsync();
                }
                break;
        }
    }

    public void RefreshPoetry()
    {
        _ = LoadPoetryAsync();
    }

    /// <summary>
    /// 生成图片并设置为系统壁纸
    /// </summary>
    public async System.Threading.Tasks.Task ApplyAsWallpaperAsync(bool silent = false)
    {
        App.Log("ApplyAsWallpaper called (Static Image Mode).");
        
        // 确保诗词已加载
        if (_currentPoetry == null)
        {
            App.Log("诗词未加载，等待加载完成...");
            await LoadPoetryAsync();
        }
        
        try
        {
            // 1. 获取屏幕逻辑尺寸
            var width = SystemParameters.PrimaryScreenWidth;
            var height = SystemParameters.PrimaryScreenHeight;

            // 2. 获取 DPI 缩放比例
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;

            // 尝试获取当前视觉对象的 DPI
            var dpi = VisualTreeHelper.GetDpi(this);
            dpiScaleX = dpi.DpiScaleX;
            dpiScaleY = dpi.DpiScaleY;
            
            // 计算物理像素尺寸
            int pixelWidth = (int)(width * dpiScaleX);
            int pixelHeight = (int)(height * dpiScaleY);
            
            App.Log($"屏幕逻辑尺寸: {width}x{height}, DPI缩放: {dpiScaleX:F2}");
            App.Log($"生成壁纸像素尺寸: {pixelWidth}x{pixelHeight}");
            
            // 获取根元素（Grid）
            if (Content is FrameworkElement rootElement)
            {
                // 设置逻辑尺寸进行布局
                rootElement.Width = width;
                rootElement.Height = height;
                
                // 强制重新测量和排列
                rootElement.Measure(new Size(width, height));
                rootElement.Arrange(new Rect(0, 0, width, height));
                rootElement.UpdateLayout();
                
                App.Log($"布局更新完成: {width}x{height}");

                // 3. 使用实际 DPI 和物理像素尺寸进行渲染
                var renderBitmap = new RenderTargetBitmap(
                    pixelWidth, 
                    pixelHeight, 
                    96.0 * dpiScaleX, 
                    96.0 * dpiScaleY, 
                    PixelFormats.Pbgra32);
                
                renderBitmap.Render(rootElement); 

                // 3. 保存为文件
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wanzhi");
                Directory.CreateDirectory(appDataPath);
                var wallpaperPath = Path.Combine(appDataPath, "wallpaper.png");
                
                using (var fileStream = new FileStream(wallpaperPath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                    encoder.Save(fileStream);
                }
                
                App.Log($"壁纸图片已保存: {wallpaperPath}");

                // 4. 设置为系统壁纸
                int result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                
                if (result != 0)
                {
                    App.Log("系统壁纸设置成功");
                    if (!silent)
                    {
                        MessageBox.Show("壁纸设置成功！\n\n已生成图片并设置为系统桌面壁纸。", "万枝", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    App.Log($"系统壁纸设置失败，错误代码: {error}");
                    if (!silent)
                    {
                        MessageBox.Show($"设置壁纸失败，错误代码: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                throw new Exception("无法获取窗口内容进行渲染");
            }
        }
        catch (Exception ex)
        {
            App.Log($"生成壁纸时发生错误: {ex.Message}");
            if (!silent)
            {
                MessageBox.Show($"生成壁纸失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}