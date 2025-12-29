using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Wanzhi.Settings;
using Wanzhi.SystemIntegration;

namespace Wanzhi;

/// <summary>
/// 应用程序入口
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;

    private string _workerMode = "settings";
    private bool _silent;
    private string _pipeName = "WanzhiSettingsPipe";
    private CancellationTokenSource? _settingsPipeCts;

    public App()
    {
        // 全局异常捕获
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void ParseWorkerArgs(string[] args)
    {
        string? mode = null;
        string? silent = null;
        string? pipe = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.Equals(a, "--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                mode = args[++i];
                continue;
            }

            if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                silent = args[++i];
                continue;
            }

            if (string.Equals(a, "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                pipe = args[++i];
                continue;
            }
        }

        if (!string.IsNullOrWhiteSpace(mode))
        {
            _workerMode = mode;
        }

        if (!string.IsNullOrWhiteSpace(silent) && bool.TryParse(silent, out var parsedSilent))
        {
            _silent = parsedSilent;
        }

        if (!string.IsNullOrWhiteSpace(pipe))
        {
            _pipeName = pipe;
        }
    }

    private static bool TryActivateExistingSettings(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(150);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("activate");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RunSettingsPipeServerAsync(string pipeName, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync();
                if (string.Equals(line, "activate", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(ActivateSettings);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    private void ActivateSettings()
    {
        if (_settingsWindow == null)
        {
            ShowSettings();
            return;
        }

        _settingsWindow.RefreshState();
        _settingsWindow.Show();
        _settingsWindow.Activate();

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        Log("Application Starting...");
        
        base.OnStartup(e);

        ParseWorkerArgs(e.Args);

        try 
        {
            if (string.Equals(_workerMode, "apply", StringComparison.OrdinalIgnoreCase))
            {
                var ok = ApplyCachedWallpaper(silent: _silent);
                if (!ok && !_silent)
                {
                    MessageBox.Show("未找到已生成的壁纸缓存。\n\n请先使用“刷新诗词”或在设置中预览生成一次壁纸。", "万枝", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                Shutdown();
                return;
            }

            if (string.Equals(_workerMode, "refresh", StringComparison.OrdinalIgnoreCase))
            {
                Log("Creating MainWindow...");
                _mainWindow = new MainWindow(startupUpdateBackground: false, startupLoadPoetry: false);
                this.MainWindow = _mainWindow;
                Log("MainWindow created.");

                _mainWindow.Hide();
                await _mainWindow.ApplyWallpaperWithPoetryAsync(updateBackground: false, silent: _silent);
                _mainWindow.Close();
                Shutdown();
                return;
            }

            // 确保单实例运行
            const string mutexName = "WanzhiWallpaperApp";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                Log("Application already running. Activating settings via pipe.");
                if (TryActivateExistingSettings(_pipeName))
                {
                    Shutdown();
                    return;
                }

                MessageBox.Show("万枝已在运行中！", "万枝", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            _settingsPipeCts = new CancellationTokenSource();
            _ = RunSettingsPipeServerAsync(_pipeName, _settingsPipeCts.Token);

            // 直接显示设置窗口
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_mainWindow == null)
                {
                    _mainWindow = new MainWindow(startupUpdateBackground: false, startupLoadPoetry: false);
                    _mainWindow.Hide();
                }

                ShowSettings();
                if (_settingsWindow != null)
                {
                    this.MainWindow = _settingsWindow;
                    _settingsWindow.Closed += (_, _) =>
                    {
                        try
                        {
                            _mainWindow?.Close();
                        }
                        catch
                        {
                        }
                        Shutdown();
                    };
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            Log($"Error creating MainWindow: {ex}");
            MessageBox.Show($"启动失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static string GetAppDataPath()
    {
        var appDataPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wanzhi");
        System.IO.Directory.CreateDirectory(appDataPath);
        return appDataPath;
    }

    private static bool ApplyCachedWallpaper(bool silent)
    {
        try
        {
            var appDataPath = GetAppDataPath();
            var singlePath = System.IO.Path.Combine(appDataPath, "wallpaper.png");

            using var desktopWallpaper = new DesktopWallpaperManager();
            desktopWallpaper.SetPosition(DesktopWallpaperManager.DESKTOP_WALLPAPER_POSITION.Fill);

            var monitorCount = desktopWallpaper.MonitorCount;
            if (monitorCount == 0)
            {
                return false;
            }

            var hasAnyPerMonitor = false;
            for (uint i = 0; i < monitorCount; i++)
            {
                var per = System.IO.Path.Combine(appDataPath, $"wallpaper_{i}.png");
                if (File.Exists(per))
                {
                    hasAnyPerMonitor = true;
                    break;
                }
            }

            if (hasAnyPerMonitor)
            {
                var allOk = true;
                for (uint i = 0; i < monitorCount; i++)
                {
                    var monitorId = desktopWallpaper.GetMonitorDevicePathAt(i);
                    var per = System.IO.Path.Combine(appDataPath, $"wallpaper_{i}.png");
                    var path = File.Exists(per) ? per : (File.Exists(singlePath) ? singlePath : null);
                    if (path == null)
                    {
                        allOk = false;
                        continue;
                    }
                    desktopWallpaper.SetWallpaper(monitorId, path);
                }
                return allOk;
            }

            if (!File.Exists(singlePath))
            {
                return false;
            }

            for (uint i = 0; i < monitorCount; i++)
            {
                var monitorId = desktopWallpaper.GetMonitorDevicePathAt(i);
                desktopWallpaper.SetWallpaper(monitorId, singlePath);
            }

            if (!silent)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e)
    {
        ShowSettings();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettings();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.RefreshPoetry();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        // 允许关闭设置窗口
        if (_settingsWindow != null)
        {
            _settingsWindow.CanClose = true;
            _settingsWindow.Close();
        }
        Shutdown();
    }

    private void ShowSettings()
    {
        try
        {
            Log("ShowSettings called.");
            
            // 如果窗口已存在且已加载，直接激活
            if (_settingsWindow != null && _settingsWindow.IsLoaded)
            {
                Log("SettingsWindow already exists, activating.");
                _settingsWindow.CanClose = true;
                _settingsWindow.RefreshState(); // 刷新状态以显示最新设置
                _settingsWindow.Show();
                _settingsWindow.Activate();
                
                // 如果之前最小化了，还原它
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                return;
            }

            // 否则创建新窗口
            Log("Creating new SettingsWindow.");
            _settingsWindow = new SettingsWindow();
            _settingsWindow.CanClose = true;
            
            if (_mainWindow != null && _mainWindow.IsLoaded)
            {
                _settingsWindow.Owner = _mainWindow;
                Log("Set SettingsWindow owner to MainWindow.");
            }
            
            _settingsWindow.Show();
            Log("SettingsWindow shown.");
        }
        catch (Exception ex)
        {
            Log($"Error showing settings: {ex}");
            Log($"Stack trace: {ex.StackTrace}");
            MessageBox.Show($"无法打开设置窗口: {ex.Message}\n\n详细信息:\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async System.Threading.Tasks.Task ShowMainWindowAsync()
    {
        try
        {
            Log("ShowMainWindow called.");
            var window = _mainWindow;
            var created = false;
            if (window == null)
            {
                window = new MainWindow(startupUpdateBackground: false, startupLoadPoetry: false);
                created = true;
            }

            Log("Applying static wallpaper.");
            window.Hide();
            await window.ApplyAsWallpaperAsync(silent: _silent);

            Log("MainWindow wallpaper applied or attached.");

            if (created)
            {
                window.Close();
            }
        }
        catch (Exception ex)
        {
            Log($"Error showing MainWindow: {ex}");
            MessageBox.Show($"无法显示壁纸: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log($"Dispatcher Unhandled Exception: {e.Exception}");
        MessageBox.Show($"发生未处理的错误: {e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log($"Domain Unhandled Exception: {e.ExceptionObject}");
        MessageBox.Show($"发生严重错误: {e.ExceptionObject}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void Log(string message)
    {
        try
        {
            string logFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wanzhi_debug.log");
            System.IO.File.AppendAllText(logFile, $"{DateTime.Now}: {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging errors
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log("Application Exiting.");
        // 清理资源
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        try
        {
            _settingsPipeCts?.Cancel();
            _settingsPipeCts?.Dispose();
        }
        catch
        {
        }

        // 保存设置
        AppSettings.Instance.Save();

        base.OnExit(e);
    }
}
