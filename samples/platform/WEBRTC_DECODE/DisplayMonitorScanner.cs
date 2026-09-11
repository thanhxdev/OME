using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WEBRTC_DECODE
{
    public sealed class DisplayMonitorInfo
    {
        public int Index { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRateHz { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public bool IsPrimary { get; set; }

        public string DisplayLabel =>
            $"🖥️ Display {Index + 1}: {FriendlyName} [{Width}x{Height} @ {RefreshRateHz}Hz]{(IsPrimary ? " (Primary)" : "")}";

        public override string ToString() => DisplayLabel;
    }

    public static class DisplayMonitorScanner
    {
        #region Win32 Interop

        private const int CCHDEVICENAME = 32;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        private const int MONITORINFOF_PRIMARY = 1;
        private const int ENUM_CURRENT_SETTINGS = -1;

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        #endregion

        public static Task<List<DisplayMonitorInfo>> ScanMonitorsAsync()
        {
            return Task.Run(() => ScanMonitors());
        }

        public static List<DisplayMonitorInfo> ScanMonitors()
        {
            var monitors = new List<DisplayMonitorInfo>();
            int monitorIndex = 0;

            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                {
                    var mi = new MONITORINFOEX();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));

                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        string devName = mi.szDevice ?? string.Empty;
                        bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                        int width = Math.Abs(mi.rcMonitor.right - mi.rcMonitor.left);
                        int height = Math.Abs(mi.rcMonitor.bottom - mi.rcMonitor.top);
                        int refreshRate = 60;

                        // Query active display settings (Resolution & Refresh Rate)
                        var dm = new DEVMODE();
                        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                        if (EnumDisplaySettings(devName, ENUM_CURRENT_SETTINGS, ref dm))
                        {
                            if (dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                            {
                                width = dm.dmPelsWidth;
                                height = dm.dmPelsHeight;
                            }
                            if (dm.dmDisplayFrequency > 0)
                            {
                                refreshRate = dm.dmDisplayFrequency;
                            }
                        }

                        // Query Friendly Name from display devices
                        string friendlyName = GetFriendlyMonitorName(devName);
                        if (string.IsNullOrWhiteSpace(friendlyName))
                        {
                            friendlyName = isPrimary ? "Primary Display" : $"External Display {monitorIndex + 1}";
                        }

                        monitors.Add(new DisplayMonitorInfo
                        {
                            Index = monitorIndex++,
                            DeviceName = devName,
                            FriendlyName = friendlyName,
                            Width = width,
                            Height = height,
                            RefreshRateHz = refreshRate,
                            Left = mi.rcMonitor.left,
                            Top = mi.rcMonitor.top,
                            IsPrimary = isPrimary
                        });
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                // Fallback handled below
            }

            // Fallback if no monitor detected (virtual environment or headless)
            if (monitors.Count == 0)
            {
                monitors.Add(new DisplayMonitorInfo
                {
                    Index = 0,
                    DeviceName = @"\\.\DISPLAY1",
                    FriendlyName = "Main Screen",
                    Width = 1920,
                    Height = 1080,
                    RefreshRateHz = 60,
                    Left = 0,
                    Top = 0,
                    IsPrimary = true
                });
            }

            return monitors;
        }

        private static string GetFriendlyMonitorName(string deviceName)
        {
            try
            {
                var displayDevice = new DISPLAY_DEVICE();
                displayDevice.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));

                if (EnumDisplayDevices(deviceName, 0, ref displayDevice, 0))
                {
                    if (!string.IsNullOrWhiteSpace(displayDevice.DeviceString))
                    {
                        return displayDevice.DeviceString.Trim();
                    }
                }
            }
            catch { }

            return string.Empty;
        }
    }
}
