using System;
using System.Runtime.InteropServices;

namespace Wanzhi.SystemIntegration
{
    internal sealed class DesktopWallpaperManager : IDisposable
    {
        private readonly IDesktopWallpaper _wallpaper;
        private bool _disposed;

        public DesktopWallpaperManager()
        {
            _wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (Marshal.IsComObject(_wallpaper))
                {
                    Marshal.FinalReleaseComObject(_wallpaper);
                }
            }
            catch
            {
            }
        }

        public uint MonitorCount => _wallpaper.GetMonitorDevicePathCount();

        public string GetMonitorDevicePathAt(uint index)
        {
            return _wallpaper.GetMonitorDevicePathAt(index);
        }

        public bool TryGetMonitorRect(string monitorId, out RECT rect)
        {
            try
            {
                _wallpaper.GetMonitorRECT(monitorId, out rect);
                return true;
            }
            catch
            {
                rect = default;
                return false;
            }
        }

        public RECT GetMonitorRect(string monitorId)
        {
            if (!TryGetMonitorRect(monitorId, out var rect))
            {
                return default;
            }

            return rect;
        }

        public (int Width, int Height) GetMonitorPixelSize(string monitorId)
        {
            try
            {
                var mode = new DEVMODE();
                mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(monitorId, ENUM_CURRENT_SETTINGS, ref mode))
                {
                    if (mode.dmPelsWidth > 0 && mode.dmPelsHeight > 0)
                    {
                        return ((int)mode.dmPelsWidth, (int)mode.dmPelsHeight);
                    }
                }
            }
            catch
            {
            }

            if (TryGetMonitorRect(monitorId, out var rect))
            {
                return (rect.Width, rect.Height);
            }

            return (0, 0);
        }

        public (double ScaleX, double ScaleY) GetMonitorDpiScale(string monitorId)
        {
            var hmonitor = TryGetHMonitorByDeviceName(monitorId);
            if (hmonitor == IntPtr.Zero)
            {
                if (!TryGetMonitorRect(monitorId, out var rect))
                {
                    return (1.0, 1.0);
                }
                var nativeRect = new NativeRect
                {
                    Left = rect.Left,
                    Top = rect.Top,
                    Right = rect.Right,
                    Bottom = rect.Bottom
                };

                hmonitor = MonitorFromRect(ref nativeRect, MONITOR_DEFAULTTONEAREST);
                if (hmonitor == IntPtr.Zero)
                {
                    return (1.0, 1.0);
                }
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
        private const int ENUM_CURRENT_SETTINGS = -1;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public uint cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData);

        private IntPtr TryGetHMonitorByDeviceName(string deviceName)
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData) =>
                {
                    var mi = new MONITORINFOEX();
                    mi.cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFOEX));
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        if (string.Equals(mi.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            found = hMonitor;
                            return false;
                        }
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                return IntPtr.Zero;
            }

            return found;
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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);
    }
}
