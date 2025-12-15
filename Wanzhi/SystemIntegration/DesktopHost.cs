using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Wanzhi;

namespace Wanzhi.SystemIntegration
{
    /// <summary>
    /// 将 WPF 窗口嵌入到桌面 WorkerW，作为动态壁纸宿主。
    /// 当前仅提供基础 Attach/Detach 能力，调用方负责控制窗口内容与生命周期。
    /// </summary>
    public static class DesktopHost
    {
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private const int GWL_STYLE = -16;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CHILD = 0x40000000;

        private const uint WM_SHELLHOOKMESSAGE = 0x052C;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        private const uint SMTO_NORMAL = 0x0000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        /// <summary>
        /// 尝试获取用于承载壁纸的 WorkerW 窗口句柄。
        /// </summary>
        private static IntPtr GetDesktopHostHandle()
        {
            try
            {
                App.Log("DesktopHost: locating desktop host handle...");

                // 1. 找到 Progman
                var progman = FindWindow("Progman", null);
                App.Log($"DesktopHost: Progman handle = 0x{progman.ToInt64():X}");
                if (progman == IntPtr.Zero)
                {
                    App.Log("DesktopHost: Progman not found.");
                    return IntPtr.Zero;
                }

                // 2. 发送特殊消息，要求创建 WorkerW
                // 使用 SendMessageTimeout 避免死锁，且通常更可靠
                IntPtr result = IntPtr.Zero;
                SendMessageTimeout(progman, WM_SHELLHOOKMESSAGE, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out result);

                // 3. 遍历查找合适的 WorkerW
                IntPtr worker = IntPtr.Zero;
                IntPtr host = IntPtr.Zero;

                // 尝试多次查找，防止刚刚创建尚未就绪
                // 但通常 SendMessage 返回后应当已就绪
                
                do
                {
                    worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                    if (worker != IntPtr.Zero)
                    {
                        var defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (defView != IntPtr.Zero)
                        {
                            // 找到了包含 SHELLDLL_DefView 的 WorkerW
                            // 它的下一个 WorkerW 就是我们的宿主 (Wallpaper Host)
                            host = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                            App.Log($"DesktopHost: found WorkerW with SHELLDLL_DefView (0x{worker.ToInt64():X}), selected host = 0x{host.ToInt64():X}");
                            break;
                        }
                    }
                } while (worker != IntPtr.Zero);

                if (host == IntPtr.Zero)
                {
                    App.Log("DesktopHost: no suitable WorkerW host found.");
                }

                return host;
            }
            catch (Exception ex)
            {
                App.Log($"DesktopHost: exception in GetDesktopHostHandle: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 将指定窗口作为子窗口嵌入桌面 WorkerW。
        /// </summary>
        public static bool Attach(Window window)
        {
            try
            {
                var host = GetDesktopHostHandle();
                if (host == IntPtr.Zero)
                {
                    App.Log("DesktopHost.Attach: host handle is 0, aborting.");
                    return false;
                }

                var helper = new WindowInteropHelper(window);
                if (helper.Handle == IntPtr.Zero)
                {
                    // 确保已经初始化句柄
                    helper.EnsureHandle();
                }

                var hwnd = helper.Handle;

                App.Log($"DesktopHost.Attach: window hwnd = 0x{hwnd.ToInt64():X}, host = 0x{host.ToInt64():X}");

                // 设置父窗口为桌面宿主
                var previousParent = SetParent(hwnd, host);
                App.Log($"DesktopHost.Attach: SetParent previousParent = 0x{previousParent.ToInt64():X}");
                
                // 如果父窗口设置失败，可能是权限或兼容性问题
                if (previousParent == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    App.Log($"DesktopHost.Attach: SetParent failed with error {Marshal.GetLastWin32Error()}");
                }

                // 设置为子窗口样式
                int style = GetWindowLong(hwnd, GWL_STYLE);
                style |= WS_VISIBLE | WS_CHILD;
                SetWindowLong(hwnd, GWL_STYLE, style);

                // 让窗口填满主屏，从左上角开始
                window.WindowState = WindowState.Normal;
                window.Left = 0;
                window.Top = 0;
                window.Width = SystemParameters.PrimaryScreenWidth;
                window.Height = SystemParameters.PrimaryScreenHeight;

                // 显示窗口
                ShowWindow(hwnd, SW_SHOW);

                App.Log("DesktopHost.Attach: attach completed.");

                return true;
            }
            catch
            {
                App.Log("DesktopHost.Attach: exception thrown, returning false.");
                return false;
            }
        }

        /// <summary>
        /// 将窗口从桌面宿主中移除，恢复原始父窗口。
        /// </summary>
        public static void Detach(Window window)
        {
            try
            {
                var helper = new WindowInteropHelper(window);
                if (helper.Handle == IntPtr.Zero)
                {
                    return;
                }

                var hwnd = helper.Handle;
                // 传入 NULL 作为父窗口句柄，恢复为顶层窗口
                SetParent(hwnd, IntPtr.Zero);
            }
            catch
            {
                // 忽略清理异常
            }
        }
    }
}
