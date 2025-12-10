using System;
using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
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
    private TaskbarIcon? _trayIcon;

    public App()
    {
        // 全局异常捕获
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("Application Starting...");
        
        // 确保单实例运行
        const string mutexName = "WanzhiWallpaperApp";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            Log("Application already running. Exiting.");
            MessageBox.Show("万枝已在运行中！", "万枝", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try 
        {
            Log("Creating MainWindow...");
            // 创建主窗口但不显示
            _mainWindow = new MainWindow();
            this.MainWindow = _mainWindow; // 设置为主窗口，防止程序自动退出
            // 不要立即调用 Hide()，让 Window_Loaded 有机会触发
            Log("MainWindow created.");

            // 初始化系统托盘图标
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            Log("TrayIcon initialized.");

            // 直接显示设置窗口
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowSettings();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            Log($"Error creating MainWindow: {ex}");
            MessageBox.Show($"启动失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
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
        Shutdown();
    }

    private void ShowSettings()
    {
        try
        {
            Log("ShowSettings called.");
            Log($"MainWindow is null: {_mainWindow == null}");
            Log($"MainWindow is loaded: {_mainWindow?.IsLoaded}");
            
            var settingsWindow = new SettingsWindow();
            Log("SettingsWindow created.");
            
            if (_mainWindow != null && _mainWindow.IsLoaded)
            {
                settingsWindow.Owner = _mainWindow;
                Log("Set SettingsWindow owner to MainWindow.");
            }
            else
            {
                Log("MainWindow not ready, showing SettingsWindow without owner.");
            }
            
            settingsWindow.ShowDialog();
            Log("SettingsWindow closed.");
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
            if (_mainWindow != null)
            {
                var settings = AppSettings.Instance;

                if (settings.WallpaperMode == WallpaperMode.Dynamic)
                {
                    Log("Using Dynamic Wallpaper Mode.");

                    // 确保窗口初始化完成
                    if (!_mainWindow.IsLoaded)
                    {
                        _mainWindow.Show();
                    }
                    else
                    {
                        _mainWindow.Show();
                    }

                    // 尝试将窗口嵌入桌面
                    var attached = DesktopHost.Attach(_mainWindow);
                    if (!attached)
                    {
                        Log("DesktopHost.Attach failed, falling back to static wallpaper mode.");
                        settings.WallpaperMode = WallpaperMode.Static;
                        await _mainWindow.ApplyAsWallpaperAsync();
                    }
                }
                else
                {
                    Log("Using Static Wallpaper Mode.");
                    // 切换回静态壁纸前，确保从桌面宿主中移除动态窗口
                    DesktopHost.Detach(_mainWindow);
                    _mainWindow.Hide();
                    // 应用为壁纸（包含显示逻辑）
                    await _mainWindow.ApplyAsWallpaperAsync();
                }

                _mainWindow.Activate();
                Log("MainWindow wallpaper applied or attached and activated.");
            }
            else
            {
                Log("MainWindow is null, cannot show.");
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

        // 保存设置
        AppSettings.Instance.Save();

        base.OnExit(e);
    }
}
