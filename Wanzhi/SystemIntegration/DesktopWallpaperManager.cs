using System;
using System.Runtime.InteropServices;

namespace Wanzhi.SystemIntegration
{
    internal sealed class DesktopWallpaperManager
    {
        private readonly IDesktopWallpaper _wallpaper;

        public DesktopWallpaperManager()
        {
            _wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
        }

        public uint MonitorCount => _wallpaper.GetMonitorDevicePathCount();

        public string GetMonitorDevicePathAt(uint index)
        {
            return _wallpaper.GetMonitorDevicePathAt(index);
        }

        public RECT GetMonitorRect(string monitorId)
        {
            _wallpaper.GetMonitorRECT(monitorId, out var rect);
            return rect;
        }

        public (double ScaleX, double ScaleY) GetMonitorDpiScale(string monitorId)
        {
            var rect = GetMonitorRect(monitorId);
            var nativeRect = new NativeRect
            {
                Left = rect.Left,
                Top = rect.Top,
                Right = rect.Right,
                Bottom = rect.Bottom
            };

            var hmonitor = MonitorFromRect(ref nativeRect, MONITOR_DEFAULTTONEAREST);
            if (hmonitor == IntPtr.Zero)
            {
                return (1.0, 1.0);
            }

            try
            {
                uint dpiX;
                uint dpiY;
                int hr = GetDpiForMonitor(hmonitor, MonitorDpiType.EffectiveDpi, out dpiX, out dpiY);
                if (hr != 0 || dpiX == 0 || dpiY == 0)
                {
                    return (1.0, 1.0);
                }

                return (dpiX / 96.0, dpiY / 96.0);
            }
            catch
            {
                return (1.0, 1.0);
            }
        }

        public void SetWallpaper(string monitorId, string wallpaperPath)
        {
            _wallpaper.SetWallpaper(monitorId, wallpaperPath);
        }

        public void SetPosition(DESKTOP_WALLPAPER_POSITION position)
        {
            _wallpaper.SetPosition(position);
        }

        [ComImport]
        [Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass
        {
        }

        [ComImport]
        [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(DESKTOP_WALLPAPER_POSITION position);
            DESKTOP_WALLPAPER_POSITION GetPosition();
            void SetSlideshow(IntPtr items);
            IntPtr GetSlideshow();
            void SetSlideshowOptions(uint options, uint slideshowTick);
            void GetSlideshowOptions(out uint options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, uint direction);
            DESKTOP_SLIDESHOW_STATE GetStatus();
            bool Enable(bool enable);
        }

        internal enum DESKTOP_WALLPAPER_POSITION
        {
            Center = 0,
            Tile = 1,
            Stretch = 2,
            Fit = 3,
            Fill = 4,
            Span = 5
        }

        [Flags]
        internal enum DESKTOP_SLIDESHOW_STATE
        {
            Enabled = 0x00000001,
            Slideshow = 0x00000002,
            DisabledByRemoteSession = 0x00000004
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private enum MonitorDpiType
        {
            EffectiveDpi = 0,
            AngularDpi = 1,
            RawDpi = 2,
            Default = EffectiveDpi
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect([In] ref NativeRect lprc, uint dwFlags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);
    }
}
