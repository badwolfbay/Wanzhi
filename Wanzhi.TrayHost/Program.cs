using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

using ThreadingTimer = System.Threading.Timer;

namespace Wanzhi.TrayHost;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string TrayMutexName = "WanzhiTrayHost";
    private const string DefaultSettingsPipeName = "WanzhiSettingsPipe";

    private readonly Mutex _singleInstanceMutex;
    private readonly NotifyIcon _notifyIcon;

    private FileSystemWatcher? _settingsWatcher;
    private ThreadingTimer? _settingsDebounceTimer;
    private ThreadingTimer? _autoRefreshTimer;
    private readonly object _autoRefreshGate = new object();
    private int _refreshInFlight;

    public TrayApplicationContext()
    {
        _singleInstanceMutex = new Mutex(true, TrayMutexName, out var createdNew);
        if (!createdNew)
        {
            Environment.Exit(0);
            return;
        }

        _notifyIcon = new NotifyIcon
        {
            Text = "万枝 - 诗词壁纸",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => LaunchOrActivateSettings();

        StartOrUpdateAutoRefresh();
        StartSettingsWatcher();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            try { _settingsWatcher?.Dispose(); } catch { }
            try { _settingsDebounceTimer?.Dispose(); } catch { }
            try { _autoRefreshTimer?.Dispose(); } catch { }

            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Icon LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var settings = new ToolStripMenuItem("设置(&S)");
        settings.Click += (_, _) => LaunchOrActivateSettings();

        var refresh = new ToolStripMenuItem("刷新诗词(&R)");
        refresh.Click += (_, _) => LaunchWorker("refresh", silent: true);

        var exit = new ToolStripMenuItem("退出(&X)");
        exit.Click += (_, _) => ExitApplication();

        menu.Items.Add(settings);
        menu.Items.Add(refresh);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        return menu;
    }

    private void ExitApplication()
    {
        ExitThread();
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wanzhi",
            "settings.json");
    }

    private static int ReadRefreshIntervalMinutes()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return 60;
            }

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("RefreshIntervalMinutes", out var prop)
                && prop.ValueKind == JsonValueKind.Number
                && prop.TryGetInt32(out var minutes))
            {
                return minutes;
            }
        }
        catch
        {
        }

        return 60;
    }

    private void StartOrUpdateAutoRefresh()
    {
        lock (_autoRefreshGate)
        {
            var minutes = ReadRefreshIntervalMinutes();
            if (minutes <= 0)
            {
                _autoRefreshTimer?.Dispose();
                _autoRefreshTimer = null;
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, minutes));
            _autoRefreshTimer?.Dispose();
            _autoRefreshTimer = new ThreadingTimer(_ => TriggerAutoRefresh(), null, interval, interval);
        }
    }

    private void TriggerAutoRefresh()
    {
        if (Interlocked.Exchange(ref _refreshInFlight, 1) == 1)
        {
            return;
        }

        try
        {
            LaunchWorker("refresh", silent: true);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInFlight, 0);
        }
    }

    private void StartSettingsWatcher()
    {
        try
        {
            var path = GetSettingsPath();
            var dir = Path.GetDirectoryName(path);
            var file = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(file))
            {
                return;
            }

            Directory.CreateDirectory(dir);

            _settingsDebounceTimer = new ThreadingTimer(_ =>
            {
                try
                {
                    StartOrUpdateAutoRefresh();
                }
                catch
                {
                }
            });

            void schedule()
            {
                try
                {
                    _settingsDebounceTimer?.Change(400, Timeout.Infinite);
                }
                catch
                {
                }
            }

            _settingsWatcher = new FileSystemWatcher(dir)
            {
                Filter = file,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            _settingsWatcher.Changed += (_, _) => schedule();
            _settingsWatcher.Created += (_, _) => schedule();
            _settingsWatcher.Renamed += (_, _) => schedule();
            _settingsWatcher.EnableRaisingEvents = true;
        }
        catch
        {
        }
    }

    private void LaunchOrActivateSettings()
    {
        var pipeName = DefaultSettingsPipeName;

        if (TryActivateExistingSettings(pipeName))
        {
            return;
        }

        LaunchWorker("settings", silent: false, pipeName: pipeName);
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

    private static void LaunchWorker(string mode, bool silent, string? pipeName = null)
    {
        try
        {
            var exePath = ResolveWorkerPath();
            if (exePath == null)
            {
                MessageBox.Show("未找到 Wanzhi.exe（Worker）。请先编译 Wanzhi 项目，或将 TrayHost/Worker 输出到同一目录。", "万枝", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var args = $"--mode {mode} --silent {(silent ? "true" : "false")}";
            if (!string.IsNullOrWhiteSpace(pipeName))
            {
                args += $" --pipe {pipeName}";
            }

            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动 Worker 失败: {ex.Message}", "万枝", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? ResolveWorkerPath()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var current = new DirectoryInfo(baseDir);
            for (var i = 0; i < 10 && current != null; i++)
            {
                var direct = Path.Combine(current.FullName, "Wanzhi.exe");
                if (File.Exists(direct)) return direct;

                var release = Path.Combine(current.FullName, "Wanzhi", "bin", "Release", "net8.0-windows", "Wanzhi.exe");
                if (File.Exists(release)) return release;

                var debug = Path.Combine(current.FullName, "Wanzhi", "bin", "Debug", "net8.0-windows", "Wanzhi.exe");
                if (File.Exists(debug)) return debug;

                current = current.Parent;
            }
        }
        catch
        {
        }

        return null;
    }
}

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
