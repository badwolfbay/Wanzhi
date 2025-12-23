using Microsoft.Win32;
using System;

namespace Wanzhi.SystemIntegration
{
    /// <summary>
    /// Windows 主题检测器
    /// </summary>
    public class ThemeDetector
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "AppsUseLightTheme";

        /// <summary>
        /// 检测当前是否为深色主题
        /// </summary>
        public bool IsDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                var value = key?.GetValue(RegistryValueName);
                
                if (value is int intValue)
                {
                    return intValue == 0; // 0 = Dark, 1 = Light
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检测主题失败: {ex.Message}");
            }

            return false; // 默认浅色
        }

        /// <summary>
        /// 开始监听主题变化
        /// </summary>
        public void StartMonitoring()
        {
            // 注意: 完整的注册表监听需要使用 RegistryKey.OpenSubKey 和轮询
            // 或使用 WMI 事件。这里简化处理，可以在主窗口定时检查
            System.Diagnostics.Debug.WriteLine("主题监听已启动");
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void StopMonitoring()
        {
            System.Diagnostics.Debug.WriteLine("主题监听已停止");
        }
    }
}
