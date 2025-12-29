using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;

namespace Wanzhi.SystemIntegration
{
    /// <summary>
    /// Windows 启动项管理器
    /// </summary>
    public class StartupManager
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Wanzhi";
        private const string TrayHostExeName = "Wanzhi.TrayHost.exe";

        private static string? ResolveTrayHostExePath()
        {
            try
            {
                var currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(currentExePath))
                {
                    return null;
                }

                // If we're already running as TrayHost, use it directly.
                if (string.Equals(Path.GetFileName(currentExePath), TrayHostExeName, StringComparison.OrdinalIgnoreCase))
                {
                    return currentExePath;
                }

                // Installed / published layout: TrayHost and Worker next to each other.
                var currentDir = Path.GetDirectoryName(currentExePath);
                if (!string.IsNullOrWhiteSpace(currentDir))
                {
                    var adjacent = Path.Combine(currentDir, TrayHostExeName);
                    if (File.Exists(adjacent))
                    {
                        return adjacent;
                    }
                }

                // Dev layout: search up for common bin output locations.
                var dirInfo = !string.IsNullOrWhiteSpace(currentDir)
                    ? new DirectoryInfo(currentDir)
                    : null;

                for (var depth = 0; depth < 8 && dirInfo != null; depth++, dirInfo = dirInfo.Parent)
                {
                    var root = dirInfo.FullName;
                    var candidates = new[]
                    {
                        Path.Combine(root, TrayHostExeName),
                        Path.Combine(root, "Wanzhi.TrayHost", "bin", "Release", "net8.0-windows", TrayHostExeName),
                        Path.Combine(root, "Wanzhi.TrayHost", "bin", "Debug", "net8.0-windows", TrayHostExeName)
                    };

                    foreach (var c in candidates)
                    {
                        if (File.Exists(c))
                        {
                            return c;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string? TryExtractExePathFromRunValue(object? value)
        {
            try
            {
                if (value is not string s || string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                s = s.Trim();
                if (s.StartsWith("\"", StringComparison.Ordinal))
                {
                    var end = s.IndexOf('"', 1);
                    if (end > 1)
                    {
                        return s.Substring(1, end - 1);
                    }
                }

                var space = s.IndexOf(' ');
                return space > 0 ? s.Substring(0, space) : s;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 检查是否已设置开机自启
        /// </summary>
        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                var value = key?.GetValue(AppName);
                if (value == null)
                {
                    return false;
                }

                var expected = ResolveTrayHostExePath();
                if (string.IsNullOrWhiteSpace(expected))
                {
                    return true;
                }

                var configuredExe = TryExtractExePathFromRunValue(value);
                return !string.IsNullOrWhiteSpace(configuredExe)
                       && string.Equals(
                           Path.GetFullPath(configuredExe),
                           Path.GetFullPath(expected),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检查启动项失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 设置开机自启
        /// </summary>
        public static bool SetStartup(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key == null)
                {
                    return false;
                }

                if (enable)
                {
                    // Under TrayHost+Worker architecture, autostart must launch TrayHost.
                    // If called from Worker (Settings), resolve TrayHost location instead of current process.
                    var exePath = ResolveTrayHostExePath()
                                 ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                        return true;
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置启动项失败: {ex.Message}");
            }

            return false;
        }
    }
}
