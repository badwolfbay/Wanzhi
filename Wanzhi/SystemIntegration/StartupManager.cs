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

        /// <summary>
        /// 检查是否已设置开机自启
        /// </summary>
        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                var value = key?.GetValue(AppName);
                return value != null;
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
                    // For single-file apps, Assembly.Location is empty. Use MainModule.FileName instead.
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    
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
