using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Newtonsoft.Json;
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
    private readonly bool _startupUpdateBackground;
    private readonly bool _startupLoadPoetry;
    private IBackgroundEffectRenderer? _backgroundEffectRenderer;

    private readonly DispatcherTimer _poetryRefreshTimer;
    private readonly DispatcherTimer _diagTimer;
    private readonly ThemeDetector _themeDetector;
    private bool _isDarkTheme;
    private bool _startupPoetryRetryScheduled;

    private double _waveVariationOffset;

    private bool IsDarkModeFromSettings(AppSettings settings)
    {
        return settings.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            ThemeMode.System => _themeDetector.IsDarkTheme(),
            _ => false
        };
    }

    private void UpdateDarkModeOverlay(bool isDarkTheme)
    {
        if (DarkModeOverlay == null) return;

        if (isDarkTheme)
        {
            DarkModeOverlay.Visibility = Visibility.Visible;
            DarkModeOverlay.Opacity = 0.42;
        }
        else
        {
            DarkModeOverlay.Opacity = 0;
            DarkModeOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private static uint StableHash32(string? s)
    {
        unchecked
        {
            if (string.IsNullOrEmpty(s)) return 0;
            uint hash = 2166136261;
            for (var i = 0; i < s.Length; i++)
            {
                var c = char.ToLowerInvariant(s[i]);
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }

    private static uint Mix32(uint hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 16777619;
            return hash;
        }
    }

    private static double ComputeMonitorVariationOffset(string? monitorId, DesktopWallpaperManager.RECT rect)
    {
        unchecked
        {
            var u = StableHash32(monitorId);
            u = Mix32(u, rect.Left);
            u = Mix32(u, rect.Top);
            u = Mix32(u, rect.Right);
            u = Mix32(u, rect.Bottom);
            // Map deterministic uint seed to [0, 2π)
            var t = u / (double)uint.MaxValue;
            return t * Math.PI * 2.0;
        }
    }

    private long _diagPoetryRefreshTicks;
    private long _diagSettingsChanged;
    private long _diagLoadPoetryCalls;
    private long _diagUpdatePoetryUiCalls;
    private long _diagApplyThemeCalls;
    private long _diagApplyWallpaperCalls;

    private CancellationTokenSource? _applyWallpaperCts;
    private readonly SemaphoreSlim _applyWallpaperLock = new SemaphoreSlim(1, 1);

    // Windows API for setting wallpaper
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    public MainWindow() : this(startupUpdateBackground: true, startupLoadPoetry: true)
    {
    }

    public MainWindow(bool startupUpdateBackground) : this(startupUpdateBackground, startupLoadPoetry: true)
    {
    }

    public MainWindow(bool startupUpdateBackground, bool startupLoadPoetry)
    {
        _startupUpdateBackground = startupUpdateBackground;
        _startupLoadPoetry = startupLoadPoetry;
        InitializeComponent();

        _poetryService = new JinrishiciService();
        _themeDetector = new ThemeDetector();

        // 初始化诗词刷新定时器
        _poetryRefreshTimer = new DispatcherTimer();
        _poetryRefreshTimer.Tick += async (s, e) =>
        {
            System.Threading.Interlocked.Increment(ref _diagPoetryRefreshTicks);
            var settings = AppSettings.Instance;
            await LoadPoetryAsync(updateBackground: settings.RandomTraditionalWaveColorOnRefresh);
        };

        _diagTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _diagTimer.Tick += (s, e) =>
        {
            var poetryTick = System.Threading.Interlocked.Exchange(ref _diagPoetryRefreshTicks, 0);
            var settings = System.Threading.Interlocked.Exchange(ref _diagSettingsChanged, 0);
            var load = System.Threading.Interlocked.Exchange(ref _diagLoadPoetryCalls, 0);
            var ui = System.Threading.Interlocked.Exchange(ref _diagUpdatePoetryUiCalls, 0);
            var theme = System.Threading.Interlocked.Exchange(ref _diagApplyThemeCalls, 0);
            var applyWp = System.Threading.Interlocked.Exchange(ref _diagApplyWallpaperCalls, 0);

            App.Log($"CPU diag (5s): Visible={Visibility}, IsVisible={IsVisible}, WindowState={WindowState}, poetryTimerTick={poetryTick}, settingsChanged={settings}, loadPoetry={load}, updatePoetryUI={ui}, applyTheme={theme}, applyWallpaper={applyWp}");
        };
        _diagTimer.Start();

        // 监听设置变化
        AppSettings.Instance.PropertyChanged += Settings_PropertyChanged;

        // 手动初始化应用逻辑
        _ = InitializeApp();
    }

    private System.Threading.Tasks.Task InitializeApp()
    {
        App.Log("Initializing App...");

        // 设置窗口覆盖所有显示器
        SetupWindowBounds();

        // 应用当前主题
        ApplyTheme();

        InitializeBackgroundEffectRenderer();
        WaveCanvas.Visibility = Visibility.Collapsed;

        // 加载诗词（后台异步，不阻塞启动）
        if (_startupLoadPoetry)
        {
            _ = LoadPoetryAsync(updateBackground: _startupUpdateBackground);
        }
        else
        {
            TryLoadPoetryFromCache();
        }

        // 设置诗词刷新间隔
        UpdateRefreshInterval();

        App.Log("App Initialized.");

        return System.Threading.Tasks.Task.CompletedTask;
    }

    private static string GetPoetryCachePath()
    {
        var appDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wanzhi");
        System.IO.Directory.CreateDirectory(appDataPath);
        return System.IO.Path.Combine(appDataPath, "poetry_cache.json");
    }

    private void TryLoadPoetryFromCache()
    {
        try
        {
            var path = GetPoetryCachePath();
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var cached = JsonConvert.DeserializeObject<PoetryData>(json);
            if (cached == null)
            {
                return;
            }

            _currentPoetry = cached;
            UpdatePoetryDisplay(cached, updateBackground: false);
        }
        catch
        {
        }
    }

    private static void TrySavePoetryToCache(PoetryData poetry)
    {
        try
        {
            var path = GetPoetryCachePath();
            var json = JsonConvert.SerializeObject(poetry);
            File.WriteAllText(path, json);
        }
        catch
        {
        }
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
        _poetryRefreshTimer.Stop();
        _diagTimer.Stop();
    }

    private void InitializeBackgroundEffectRenderer()
    {
        var width = SystemParameters.PrimaryScreenWidth;
        var settings = AppSettings.Instance;
        var effect = settings.BackgroundEffect;

        var seedBase = settings.BackgroundSeed;
        if (seedBase == 0)
        {
            seedBase = Environment.TickCount;
            settings.BackgroundSeed = seedBase;
            settings.Save();
        }

        unchecked
        {
            var u = (uint)seedBase;
            _waveVariationOffset = (u / (double)uint.MaxValue) * Math.PI * 2.0;
        }

        // Wave uses a bottom strip, other effects fill the entire screen.
        if (effect == BackgroundEffectType.Wave)
        {
            WaveCanvas.VerticalAlignment = VerticalAlignment.Bottom;
            WaveCanvas.Height = 400;
        }
        else
        {
            WaveCanvas.VerticalAlignment = VerticalAlignment.Stretch;
            WaveCanvas.ClearValue(FrameworkElement.HeightProperty);
        }

        var height = effect == BackgroundEffectType.Wave
            ? 400.0
            : (SystemParameters.PrimaryScreenHeight > 0 ? SystemParameters.PrimaryScreenHeight : 1080.0);

        IBackgroundEffectRenderer renderer = effect switch
        {
            BackgroundEffectType.Bubbles => new BubblesRenderer(width, height, 12, seedBase),
            BackgroundEffectType.Blobs => new BlobsRenderer(width, height, 10, seedBase),
            _ => new WaveRenderer(width, height, 5)
        };

        _backgroundEffectRenderer = renderer;

        WaveCanvas.Children.Clear();
        foreach (var v in renderer.GetVisuals())
        {
            WaveCanvas.Children.Add(v);
        }

        ApplyTheme();

        // 立即更新一次以生成初始形状，否则第一帧可能是空的
        var variation = _backgroundEffectRenderer as IVariationOffsetRenderer;
        if (effect == BackgroundEffectType.Wave && variation != null)
        {
            variation.SetVariationOffset(_waveVariationOffset);
        }
        _backgroundEffectRenderer.Update(0);
    }

    private void RebuildBackgroundEffectRenderer()
    {
        _backgroundEffectRenderer = null;
        InitializeBackgroundEffectRenderer();
    }

    private PoetryData? _currentPoetry;

    private async System.Threading.Tasks.Task RetryLoadPoetryOnceAfterStartupAsync()
    {
        if (_startupPoetryRetryScheduled)
        {
            return;
        }

        _startupPoetryRetryScheduled = true;
        await System.Threading.Tasks.Task.Delay(6000);
        await LoadPoetryAsync(updateBackground: false);
    }

    private async System.Threading.Tasks.Task LoadPoetryAsync(bool updateBackground, bool skipAutoApply = false)
    {
        System.Threading.Interlocked.Increment(ref _diagLoadPoetryCalls);
        PoetryData? poetry = null;
        try
        {
            App.Log("开始加载诗词...");
            // Add a timeout to prevent hanging indefinitely
            var timeoutTask = System.Threading.Tasks.Task.Delay(_currentPoetry == null ? 20000 : 10000);
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

            if (_currentPoetry == null)
            {
                _ = RetryLoadPoetryOnceAfterStartupAsync();
            }
        }

        if (poetry != null)
        {
            App.Log($"诗词加载成功: {poetry.Content.Substring(0, Math.Min(5, poetry.Content.Length))}...");
            _currentPoetry = poetry;
            TrySavePoetryToCache(poetry);
            UpdatePoetryDisplay(poetry, updateBackground);

            var settings = AppSettings.Instance;
            var randomizedWaveColor = false;
            if (updateBackground && settings.RandomTraditionalWaveColorOnRefresh)
            {
                try
                {
                    var isDarkTheme = _isDarkTheme;
                    var all = TraditionalColorPalette.GetAll();

                    var pool = all
                        .Where(c => c.Hex != null)
                        .Where(c => TraditionalColorPalette.TryParseToMediaColor(c.Hex!) != null)
                        .Where(c => isDarkTheme ? c.DarkSuitable : c.LightSuitable)
                        .ToList();

                    if (pool.Count == 0)
                    {
                        pool = all
                            .Where(c => c.Hex != null)
                            .Where(c => TraditionalColorPalette.TryParseToMediaColor(c.Hex!) != null)
                            .ToList();
                    }

                    if (pool.Count > 0)
                    {
                        var picked = pool[Random.Shared.Next(0, pool.Count)];
                        App.Log($"刷新时随机传统色: {picked.Name} {picked.Hex}");
                        settings.WaveColor = picked.Hex!;
                        settings.Save();
                        randomizedWaveColor = true;
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"随机传统色波浪失败: {ex.Message}");
                }
            }

            if (!randomizedWaveColor && !skipAutoApply)
            {
                _ = QueueApplyWallpaperAsync();
            }
        }
    }

    private async System.Threading.Tasks.Task QueueApplyWallpaperAsync()
    {
        var cts = new CancellationTokenSource();
        var prev = System.Threading.Interlocked.Exchange(ref _applyWallpaperCts, cts);
        prev?.Cancel();

        prev?.Dispose();

        try
        {
            await System.Threading.Tasks.Task.Delay(250, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var op = Dispatcher.InvokeAsync(() => ApplyAsWallpaperAsync(silent: true), DispatcherPriority.Background);
        var inner = await op.Task;
        await inner;
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

    private static string GetPoetryMainText(PoetryData poetry, AppSettings settings)
    {
        if (settings.ShowFullPoetry)
        {
            var lines = poetry.Origin?.Content;
            if (lines != null)
            {
                var cleaned = lines
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l.Trim())
                    .ToList();

                if (cleaned.Count > 0)
                {
                    return string.Join("\n", cleaned);
                }
            }
        }

        return poetry.Content ?? string.Empty;
    }

    private void UpdatePoetryDisplay(PoetryData poetry, bool updateBackground)
    {
        System.Threading.Interlocked.Increment(ref _diagUpdatePoetryUiCalls);
        Dispatcher.Invoke(() =>
        {
            PoetryContainer.Children.Clear();
            App.Log("正在更新诗词 UI...");

            // Store current poetry for re-rendering on theme change
            _currentPoetry = poetry;

            // Determine text color based on background
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

            // 确定当前主题 (基于背景亮度)
            // 计算相对亮度: 0.299*R + 0.587*G + 0.114*B
            _isDarkTheme = IsDarkModeFromSettings(settings);
            UpdateDarkModeOverlay(_isDarkTheme);

            var textColorVal = _isDarkTheme ? Colors.White : GetLegibleTextColor(bgColor);
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

            UpdateTraditionalColorNameOverlay();
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
        var settings = AppSettings.Instance;
        var mainText = GetPoetryMainText(poetry, settings);
        var sentences = mainText.Split(new[] { '，', '。', '？', '！', '；', ',', '.', '?', '!', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

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
            titleStack.Children.Add(new TextBlock { Text = "﹁", FontSize = 16, FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily), Foreground = secondaryTextColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) });

            foreach (var c in poetry.Origin.Title)
            {
                titleStack.Children.Add(new TextBlock { Text = c.ToString(), FontSize = AppSettings.Instance.AuthorFontSize, FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily), Foreground = secondaryTextColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) });
            }

            // 下引号 (竖排专用)
            titleStack.Children.Add(new TextBlock { Text = "﹂", FontSize = AppSettings.Instance.AuthorFontSize, FontFamily = new FontFamily(AppSettings.Instance.AuthorFontFamily), Foreground = secondaryTextColor, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });

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
                    Margin = new Thickness(0, 1, 0, 1)
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
        var settings = AppSettings.Instance;
        var mainText = GetPoetryMainText(poetry, settings);
        var sentences = mainText.Split(new[] { '，', '。', '？', '！', '；', ',', '.', '?', '!', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

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

    private static TraditionalColor? TryGetCurrentTraditionalWaveColor()
    {
        try
        {
            var wave = AppSettings.Instance.WaveColor;
            if (string.IsNullOrWhiteSpace(wave)) return null;

            var waveColor = TraditionalColorPalette.TryParseToMediaColor(wave);
            if (waveColor == null) return null;

            var all = TraditionalColorPalette.GetAll();
            foreach (var c in all)
            {
                if (string.IsNullOrWhiteSpace(c.Hex)) continue;
                var candidateColor = TraditionalColorPalette.TryParseToMediaColor(c.Hex!);
                if (candidateColor == null) continue;

                if (candidateColor.Value.R == waveColor.Value.R &&
                    candidateColor.Value.G == waveColor.Value.G &&
                    candidateColor.Value.B == waveColor.Value.B)
                {
                    return c;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string ToVerticalText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return string.Join("\n", text.ToCharArray());
    }

    private void UpdateTraditionalColorNameOverlay()
    {
        if (RightTraditionalColorNameText == null) return;

        var settings = AppSettings.Instance;
        if (!settings.ShowTraditionalColorNameOnRight)
        {
            RightTraditionalColorNameText.Visibility = Visibility.Collapsed;
            RightTraditionalColorNameText.Text = string.Empty;
            return;
        }

        var match = TryGetCurrentTraditionalWaveColor();
        if (match?.Name == null || string.IsNullOrWhiteSpace(match.Name))
        {
            RightTraditionalColorNameText.Visibility = Visibility.Collapsed;
            RightTraditionalColorNameText.Text = string.Empty;
            return;
        }

        RightTraditionalColorNameText.Text = ToVerticalText(match.Name);
        RightTraditionalColorNameText.FontFamily = new FontFamily(settings.AuthorFontFamily);

        var canvasWidth = RootGrid != null && RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : (ActualWidth > 0 ? ActualWidth : Width);
        var canvasHeight = RootGrid != null && RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : (ActualHeight > 0 ? ActualHeight : Height);

        var charCount = Math.Max(1, match.Name.Length);
        var safeVerticalMargin = canvasHeight > 0 ? Math.Max(26, canvasHeight * 0.10) : 85;
        var safeRightMarginBase = canvasWidth > 0 ? Math.Max(18, canvasWidth * 0.03) : 60;
        var availableHeight = Math.Max(1, (canvasHeight > 0 ? canvasHeight : 900) - safeVerticalMargin * 2);

        // Extra safety to avoid glyph overhang clipping at top/bottom on some fonts/DPI
        var lineHeightFactor = 1.28;
        var computedFontSize = (availableHeight / (charCount * lineHeightFactor)) * 0.88;
        computedFontSize = Math.Max(70, Math.Min(240, computedFontSize));

        RightTraditionalColorNameText.FontSize = computedFontSize;
        RightTraditionalColorNameText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        RightTraditionalColorNameText.LineHeight = computedFontSize * lineHeightFactor;
        RightTraditionalColorNameText.MaxHeight = Math.Max(1, availableHeight - computedFontSize * 0.50);

        // Nudge down slightly to avoid glyph ascender clipping on some fonts (e.g. "飞")
        RightTraditionalColorNameText.RenderTransform = new TranslateTransform(0, computedFontSize * 0.08);

        var rightMargin = (int)Math.Round(safeRightMarginBase + computedFontSize * 0.25);
        RightTraditionalColorNameText.Margin = new Thickness(0, safeVerticalMargin, rightMargin, safeVerticalMargin);

        var baseColor = _isDarkTheme ? Colors.White : Colors.Black;
        RightTraditionalColorNameText.Foreground = new SolidColorBrush(Color.FromArgb(26, baseColor.R, baseColor.G, baseColor.B));
        RightTraditionalColorNameText.Visibility = Visibility.Visible;
    }

    private void ApplyTheme()
    {
        System.Threading.Interlocked.Increment(ref _diagApplyThemeCalls);
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
        _isDarkTheme = IsDarkModeFromSettings(settings);
        UpdateDarkModeOverlay(_isDarkTheme);

        // 应用背景效果颜色
        try
        {
            var waveBaseColor = (Color)ColorConverter.ConvertFromString(settings.WaveColor);
            if (_backgroundEffectRenderer != null)
            {
                _backgroundEffectRenderer.UpdateColor(waveBaseColor, _isDarkTheme);
            }
        }
        catch
        {
            // 默认深蓝色
            var defaultWaveColor = Color.FromRgb(57, 73, 171);
            if (_backgroundEffectRenderer != null)
            {
                _backgroundEffectRenderer.UpdateColor(defaultWaveColor, _isDarkTheme);
            }
        }
    }

    private void UpdateRefreshInterval()
    {
        var interval = AppSettings.Instance.RefreshIntervalMinutes;
        if (interval <= 0)
        {
            _poetryRefreshTimer.Stop();
            return;
        }

        _poetryRefreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, interval));
        _poetryRefreshTimer.Start();
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        System.Threading.Interlocked.Increment(ref _diagSettingsChanged);
        switch (e.PropertyName)
        {
            case nameof(AppSettings.BackgroundEffect):
                RebuildBackgroundEffectRenderer();

                if (AppSettings.Instance.BackgroundEffect == BackgroundEffectType.Wave)
                {
                    var variation = _backgroundEffectRenderer as IVariationOffsetRenderer;
                    if (variation != null)
                    {
                        _waveVariationOffset = Random.Shared.NextDouble() * Math.PI * 2.0;
                        variation.SetVariationOffset(_waveVariationOffset);
                        _backgroundEffectRenderer?.Update(0);
                    }
                }

                if (_currentPoetry != null)
                {
                    UpdatePoetryDisplay(_currentPoetry, updateBackground: true);
                    _ = QueueApplyWallpaperAsync();
                }
                break;

            case nameof(AppSettings.WaveColor):
                ApplyTheme();
                UpdateTraditionalColorNameOverlay();
                if (_currentPoetry != null)
                {
                    UpdatePoetryDisplay(_currentPoetry, updateBackground: true);
                    _ = QueueApplyWallpaperAsync();
                }
                break;

            case nameof(AppSettings.ShowTraditionalColorNameOnRight):
                UpdateTraditionalColorNameOverlay();
                if (_currentPoetry != null)
                {
                    _ = QueueApplyWallpaperAsync();
                }
                break;

            case nameof(AppSettings.Theme):
            case nameof(AppSettings.BackgroundColor):
                ApplyTheme();
                if (_currentPoetry != null)
                {
                    UpdatePoetryDisplay(_currentPoetry, updateBackground: true);
                    _ = QueueApplyWallpaperAsync();
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
            case nameof(AppSettings.ShowFullPoetry):
                // 重新加载诗词以应用新样式
                if (_currentPoetry != null)
                {
                    UpdatePoetryDisplay(_currentPoetry, updateBackground: false);
                    UpdateTraditionalColorNameOverlay();
                    _ = QueueApplyWallpaperAsync();
                }
                break;

            case nameof(AppSettings.PoetryCharacterSpacing):
            case nameof(AppSettings.PoetryVerticalCharacterSpacing):
            case nameof(AppSettings.VerticalPoetryOffset):
            case nameof(AppSettings.HorizontalPoetryOffset):
                // 重新加载诗词以应用新样式
                if (_currentPoetry != null)
                {
                    UpdatePoetryDisplay(_currentPoetry, updateBackground: false);
                    _ = QueueApplyWallpaperAsync();
                }
                break;
        }
    }

    public async System.Threading.Tasks.Task ApplyWallpaperWithPoetryAsync(bool updateBackground, bool silent)
    {
        await LoadPoetryAsync(updateBackground: updateBackground, skipAutoApply: true);
        await ApplyAsWallpaperAsync(silent: silent);
    }

    public void RefreshPoetry()
    {
        var settings = AppSettings.Instance;
        _ = LoadPoetryAsync(updateBackground: settings.RandomTraditionalWaveColorOnRefresh);
    }

    public async System.Threading.Tasks.Task ApplyAsWallpaperAsync(bool silent = false)
    {
        await _applyWallpaperLock.WaitAsync();
        try
        {
            TryCleanupTempWallpapers();
            await ApplyAsWallpaperInternalAsync(silent: silent);
        }
        finally
        {
            _applyWallpaperLock.Release();
        }
    }

    private async System.Threading.Tasks.Task ApplyAsWallpaperInternalAsync(bool silent)
    {
        System.Threading.Interlocked.Increment(ref _diagApplyWallpaperCalls);
        var applyBatchId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");

        App.Log($"ApplyAsWallpaper called (Static Image Mode). batch={applyBatchId}");

        var appDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wanzhi");
        System.IO.Directory.CreateDirectory(appDataPath);

        void CleanupBatchTempFiles()
        {
            try
            {
                var patterns = new[]
                {
                    $"wallpaper_*_{applyBatchId}.tmp.png",
                    $"wallpaper_{applyBatchId}.tmp.png"
                };

                foreach (var pattern in patterns)
                {
                    string[] files;
                    try
                    {
                        files = System.IO.Directory.GetFiles(appDataPath, pattern);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var f in files)
                    {
                        try { System.IO.File.Delete(f); } catch { }
                    }
                }
            }
            catch
            {
            }
        }

        void ReplaceFile(string tempPath, string finalPath)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(finalPath) ?? string.Empty);
                File.Move(tempPath, finalPath, overwrite: true);
            }
            catch
            {
                File.Copy(tempPath, finalPath, overwrite: true);
                try { File.Delete(tempPath); } catch { }
            }
        }

        // 确保诗词已加载
        if (_currentPoetry == null)
        {
            return;
        }

        var prevWaveVisibility = WaveCanvas.Visibility;
        try
        {
            WaveCanvas.Visibility = Visibility.Visible;
            if (_backgroundEffectRenderer == null)
            {
                InitializeBackgroundEffectRenderer();
            }

            using (var desktopWallpaper = new DesktopWallpaperManager())
            {
                var distinctMonitorRects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (uint i = 0; i < desktopWallpaper.MonitorCount; i++)
                {
                    try
                    {
                        var monitorId = desktopWallpaper.GetMonitorDevicePathAt(i);
                        if (string.IsNullOrWhiteSpace(monitorId))
                        {
                            continue;
                        }

                        var rect = desktopWallpaper.GetMonitorRect(monitorId);
                        if (rect.Width <= 0 || rect.Height <= 0)
                        {
                            continue;
                        }

                        distinctMonitorRects.Add($"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
                    }
                    catch
                    {
                    }
                }

                App.Log($"Multi-monitor detected: count={desktopWallpaper.MonitorCount}, distinctRects={distinctMonitorRects.Count}");
                if (distinctMonitorRects.Count > 1)
                {
                    desktopWallpaper.SetPosition(DesktopWallpaperManager.DESKTOP_WALLPAPER_POSITION.Fill);

                    if (Content is not FrameworkElement rootElement)
                    {
                        throw new Exception("无法获取窗口内容进行渲染");
                    }

                    App.Log($"Multi-monitor wallpaper output dir: {appDataPath}");

                    var batchId = applyBatchId;

                    var monitorWallpapers = new System.Collections.Generic.List<(string MonitorId, string FilePath)>();

                    var originalWidth = rootElement.Width;
                    var originalHeight = rootElement.Height;

                    var variationRenderer = _backgroundEffectRenderer as IVariationOffsetRenderer;
                    var originalVariationOffset = _waveVariationOffset;

                    try
                    {
                        for (uint i = 0; i < desktopWallpaper.MonitorCount; i++)
                        {
                            var monitorId = desktopWallpaper.GetMonitorDevicePathAt(i);
                            var rect = desktopWallpaper.GetMonitorRect(monitorId);
                            var (monitorPixelWidth, monitorPixelHeight) = desktopWallpaper.GetMonitorPixelSize(monitorId);
                            var (scaleX, scaleY) = desktopWallpaper.GetMonitorDpiScale(monitorId);

                            App.Log($"Monitor[{i}]: id={monitorId}, rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}, rectPx={rect.Width}x{rect.Height}, modePx={monitorPixelWidth}x{monitorPixelHeight}, scale={scaleX:F2}x{scaleY:F2}");

                            var pixelWidth = monitorPixelWidth;
                            var pixelHeight = monitorPixelHeight;
                            if (pixelWidth <= 0 || pixelHeight <= 0)
                            {
                                continue;
                            }

                            var logicalWidth = pixelWidth / Math.Max(scaleX, 0.01);
                            var logicalHeight = pixelHeight / Math.Max(scaleY, 0.01);

                            rootElement.Width = logicalWidth;
                            rootElement.Height = logicalHeight;
                            rootElement.Measure(new Size(logicalWidth, logicalHeight));
                            rootElement.Arrange(new Rect(0, 0, logicalWidth, logicalHeight));
                            rootElement.UpdateLayout();

                            // Ensure layout/render pipeline is flushed before capturing
                            await Dispatcher.Yield(DispatcherPriority.Render);

                            try
                            {
                                WaveCanvas.ClearValue(FrameworkElement.WidthProperty);
                                WaveCanvas.HorizontalAlignment = HorizontalAlignment.Stretch;
                                WaveCanvas.UpdateLayout();

                                if (_backgroundEffectRenderer != null)
                                {
                                    if (variationRenderer != null)
                                    {
                                        var monitorOffset = ComputeMonitorVariationOffset(monitorId, rect);
                                        // Add a base offset so each apply/theme change can still influence the overall feel,
                                        // but each monitor stays distinct.
                                        variationRenderer.SetVariationOffset(originalVariationOffset + monitorOffset);
                                    }

                                    var waveWidth = WaveCanvas.ActualWidth > 0
                                        ? WaveCanvas.ActualWidth
                                        : (rootElement.ActualWidth > 0 ? rootElement.ActualWidth : logicalWidth);

                                    var waveHeight = WaveCanvas.ActualHeight > 0
                                        ? WaveCanvas.ActualHeight
                                        : (WaveCanvas.Height > 0 ? WaveCanvas.Height : (rootElement.ActualHeight > 0 ? rootElement.ActualHeight : logicalHeight));

                                    _backgroundEffectRenderer.SetCanvasSize(waveWidth, waveHeight);
                                    _backgroundEffectRenderer.Update(0);
                                }

                                rootElement.UpdateLayout();

                                // Do not recompute overlay during apply; capture current UI state

                                var renderBitmap = new RenderTargetBitmap(
                                    pixelWidth,
                                    pixelHeight,
                                    96.0 * scaleX,
                                    96.0 * scaleY,
                                    PixelFormats.Pbgra32);

                                renderBitmap.Render(rootElement);

                                // Freeze before handing over to background thread for encoding/writing.
                                renderBitmap.Freeze();

                                var tempWallpaperPath = System.IO.Path.Combine(appDataPath, $"wallpaper_{i}_{batchId}.tmp.png");
                                var finalWallpaperPath = System.IO.Path.Combine(appDataPath, $"wallpaper_{i}.png");

                                var frozenBitmap = renderBitmap;
                                await System.Threading.Tasks.Task.Run(() =>
                                {
                                    using (var fileStream = new System.IO.FileStream(tempWallpaperPath, System.IO.FileMode.Create))
                                    {
                                        var encoder = new PngBitmapEncoder();
                                        encoder.Frames.Add(BitmapFrame.Create(frozenBitmap));
                                        encoder.Save(fileStream);
                                    }

                                    ReplaceFile(tempWallpaperPath, finalWallpaperPath);
                                });

                                monitorWallpapers.Add((monitorId ?? string.Empty, finalWallpaperPath));

                                await Dispatcher.Yield(DispatcherPriority.Background);
                            }
                            catch
                            {
                            }
                        }

                        // Apply wallpapers after all files are fully written to reduce flicker/intermediate state
                        for (var j = 0; j < monitorWallpapers.Count; j++)
                        {
                            var (monitorId, wallpaperPath) = monitorWallpapers[j];
                            App.Log($"Set monitor wallpaper (batched): index={j}, batch={batchId}, path={wallpaperPath}");
                            desktopWallpaper.SetWallpaper(monitorId, wallpaperPath);
                        }
                    }
                    finally
                    {
                        if (variationRenderer != null)
                        {
                            variationRenderer.SetVariationOffset(originalVariationOffset);
                        }

                        rootElement.Width = originalWidth;
                        rootElement.Height = originalHeight;
                        rootElement.UpdateLayout();
                    }

                    if (!silent)
                    {
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    }

                    App.Log("Multi-monitor wallpaper applied successfully");
                    if (!silent)
                    {
                        MessageBox.Show("壁纸设置成功！\n\n已为多屏分别生成图片并设置为系统桌面壁纸。", "万枝", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    if (Visibility != Visibility.Visible)
                    {
                        WaveCanvas.Visibility = Visibility.Collapsed;
                    }

                    CleanupBatchTempFiles();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"多屏壁纸设置失败，回退单屏逻辑: {ex}");
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
            if (Content is not FrameworkElement rootElement)
            {
                throw new Exception("无法获取窗口内容进行渲染");
            }

            // 设置逻辑尺寸进行布局
            rootElement.Width = width;
            rootElement.Height = height;

            // 强制重新测量和排列
            rootElement.Measure(new Size(width, height));
            rootElement.Arrange(new Rect(0, 0, width, height));
            rootElement.UpdateLayout();

            App.Log($"布局更新完成: {width}x{height}");

            // Ensure the selected background effect is regenerated for current size
            try
            {
                if (_backgroundEffectRenderer != null)
                {
                    var waveVariation = (_backgroundEffectRenderer as IVariationOffsetRenderer);
                    if (AppSettings.Instance.BackgroundEffect == BackgroundEffectType.Wave && waveVariation != null)
                    {
                        waveVariation.SetVariationOffset(_waveVariationOffset);
                    }

                    var waveWidth = WaveCanvas.ActualWidth > 0
                        ? WaveCanvas.ActualWidth
                        : (rootElement.ActualWidth > 0 ? rootElement.ActualWidth : width);

                    var waveHeight = WaveCanvas.ActualHeight > 0
                        ? WaveCanvas.ActualHeight
                        : (WaveCanvas.Height > 0 ? WaveCanvas.Height : (rootElement.ActualHeight > 0 ? rootElement.ActualHeight : height));
                    _backgroundEffectRenderer.SetCanvasSize(waveWidth, waveHeight);
                    _backgroundEffectRenderer.Update(0);
                }
            }
            catch
            {
            }

            // 3. 使用实际 DPI 和物理像素尺寸进行渲染
            var renderBitmap = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                96.0 * dpiScaleX,
                96.0 * dpiScaleY,
                PixelFormats.Pbgra32);

            renderBitmap.Render(rootElement);

            // Freeze before encoding on background thread.
            renderBitmap.Freeze();

            // 3. 保存为文件
            var batchId = applyBatchId;
            var tempWallpaperPath = System.IO.Path.Combine(appDataPath, $"wallpaper_{batchId}.tmp.png");
            var wallpaperPath = System.IO.Path.Combine(appDataPath, "wallpaper.png");

            var frozenBitmap = renderBitmap;
            await System.Threading.Tasks.Task.Run(() =>
            {
                using (var fileStream = new System.IO.FileStream(tempWallpaperPath, System.IO.FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frozenBitmap));
                    encoder.Save(fileStream);
                }

                ReplaceFile(tempWallpaperPath, wallpaperPath);
            });

            App.Log($"壁纸图片已保存: {wallpaperPath}");

            // 4. 设置为系统壁纸
            var winIni = SPIF_UPDATEINIFILE | (silent ? 0 : SPIF_SENDCHANGE);
            int result = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperPath, winIni);

            if (result != 0)
            {
                App.Log("系统壁纸设置成功");
                if (!silent)
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

                if (Visibility != Visibility.Visible)
                {
                    WaveCanvas.Visibility = Visibility.Collapsed;
                }

                CleanupBatchTempFiles();
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
        finally
        {
            // 恢复波浪可见性
            WaveCanvas.Visibility = prevWaveVisibility;
            CleanupBatchTempFiles();
        }
    }

    private static void TryCleanupTempWallpapers()
    {
        try
        {
            var appDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wanzhi");
            if (!System.IO.Directory.Exists(appDataPath))
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var files = System.IO.Directory.GetFiles(appDataPath, "wallpaper_*.tmp.png");
            foreach (var file in files)
            {
                try
                {
                    var age = nowUtc - System.IO.File.GetLastWriteTimeUtc(file);
                    if (age > TimeSpan.FromMinutes(5))
                    {
                        System.IO.File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}