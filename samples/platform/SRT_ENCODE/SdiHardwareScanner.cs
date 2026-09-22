using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SRT_ENCODE
{
    public enum SdiPortDirection
    {
        Input,
        Output,
        Both
    }

    public sealed class SdiDeviceInfo
    {
        public string Name { get; set; } = string.Empty;           // e.g., "DeckLink Duo (1)"
        public string DisplayLabel { get; set; } = string.Empty;   // e.g., "📡 [SDI IN] DeckLink Duo (1)"
        public SdiPortDirection Direction { get; set; }
        public bool IsPhysicalHardware { get; set; }
        public string DevicePath { get; set; } = string.Empty;

        public override string ToString() => string.IsNullOrWhiteSpace(DisplayLabel) ? Name : DisplayLabel;
    }

    public static class SdiHardwareScanner
    {
        #region COM Interfaces for DirectShow Device Enumeration Fallback

        [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            int Read([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, Out, MarshalAs(UnmanagedType.Struct)] ref object pVar, [In] IntPtr pErrorLog);

            [PreserveSig]
            int Write([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, MarshalAs(UnmanagedType.Struct)] ref object pVar);
        }

        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig]
            int CreateClassEnumerator([In] ref Guid pType, [Out] out IEnumMoniker ppEnumMoniker, [In] int dwFlags);
        }

        private static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");

        #endregion

        private static readonly Regex DecklinkDevRegex = new(@"\[decklink\s*@.*?\]\s*'(.*?)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SingleQuoteRegex = new(@"'([^']+)'", RegexOptions.Compiled);

        /// <summary>
        /// Scans for SDI Input devices using FFmpeg DeckLink probe with graceful fallback to DirectShow.
        /// Guaranteed: Never returns fake mock hardware if cards are offline.
        /// </summary>
        public static Task<List<SdiDeviceInfo>> ScanInputsAsync()
        {
            return Task.Run(() =>
            {
                var list = ProbeFfmpegDecklink(isInput: true);
                if (list.Count > 0)
                {
                    return list;
                }

                // Fallback to DirectShow
                return ScanDirectShowFallback(SdiPortDirection.Input);
            });
        }

        /// <summary>
        /// Scans for SDI Output devices using FFmpeg DeckLink probe.
        /// </summary>
        public static Task<List<SdiDeviceInfo>> ScanOutputsAsync()
        {
            return Task.Run(() =>
            {
                var list = ProbeFfmpegDecklink(isInput: false);
                if (list.Count > 0)
                {
                    return list;
                }

                // If FFmpeg decklink probe fails or finds nothing, fallback to checking physical cards via DirectShow
                var dshowDevices = ScanDirectShowFallback(SdiPortDirection.Output);
                return dshowDevices;
            });
        }

        /// <summary>
        /// Backward-compatible general scan (returns input devices by default).
        /// </summary>
        public static Task<List<SdiDeviceInfo>> ScanDevicesAsync()
        {
            return ScanInputsAsync();
        }

        /// <summary>
        /// Probes DeckLink devices directly via FFmpeg background process.
        /// Input probe:  ffmpeg -hide_banner -f decklink -list_devices 1 -i dummy
        /// Output probe: ffmpeg -hide_banner -list_devices 1 -f decklink dummy
        /// </summary>
        private static List<SdiDeviceInfo> ProbeFfmpegDecklink(bool isInput)
        {
            var results = new List<SdiDeviceInfo>();
            string args = isInput
                ? "-hide_banner -f decklink -list_devices 1 -i dummy"
                : "-hide_banner -list_devices 1 -f decklink dummy";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return results;

                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(3000);

                if (string.IsNullOrWhiteSpace(stderr)) return results;

                // If FFmpeg does not support decklink format, return empty so caller falls back
                if (stderr.Contains("Unknown input format: 'decklink'", StringComparison.OrdinalIgnoreCase) ||
                    stderr.Contains("Unknown output format: 'decklink'", StringComparison.OrdinalIgnoreCase))
                {
                    return results;
                }

                var lines = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bool inDeviceList = false;

                foreach (var line in lines)
                {
                    if (line.Contains("Blackmagic DeckLink", StringComparison.OrdinalIgnoreCase) &&
                        line.Contains("devices:", StringComparison.OrdinalIgnoreCase))
                    {
                        inDeviceList = true;
                        continue;
                    }

                    if (inDeviceList)
                    {
                        // Match '[decklink @ ...] 'DeckLink Port Name'' or ''DeckLink Port Name''
                        Match match = DecklinkDevRegex.Match(line);
                        string devName = string.Empty;

                        if (match.Success && match.Groups.Count > 1)
                        {
                            devName = match.Groups[1].Value.Trim();
                        }
                        else
                        {
                            var sqMatch = SingleQuoteRegex.Match(line);
                            if (sqMatch.Success && sqMatch.Groups.Count > 1)
                            {
                                devName = sqMatch.Groups[1].Value.Trim();
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(devName) && !results.Exists(d => d.Name.Equals(devName, StringComparison.OrdinalIgnoreCase)))
                        {
                            var dir = isInput ? SdiPortDirection.Input : SdiPortDirection.Output;
                            string prefix = isInput ? "📡 [SDI IN]" : "📡 [SDI OUT]";
                            results.Add(new SdiDeviceInfo
                            {
                                Name = devName,
                                DisplayLabel = $"{prefix} {devName}",
                                Direction = dir,
                                IsPhysicalHardware = true,
                                DevicePath = devName
                            });
                        }
                    }
                }
            }
            catch
            {
                // FFmpeg probe failed or not found
            }

            return results;
        }

        /// <summary>
        /// DirectShow COM enumeration fallback when FFmpeg DeckLink probe is not compiled or empty.
        /// </summary>
        private static List<SdiDeviceInfo> ScanDirectShowFallback(SdiPortDirection direction)
        {
            var results = new List<SdiDeviceInfo>();

            try
            {
                Type? devEnumType = Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum);
                if (devEnumType == null) return results;

                object? devEnumObj = Activator.CreateInstance(devEnumType);
                if (devEnumObj is not ICreateDevEnum devEnum) return results;

                Guid catGuid = CLSID_VideoInputDeviceCategory;
                int hr = devEnum.CreateClassEnumerator(ref catGuid, out IEnumMoniker enumMoniker, 0);

                if (hr == 0 && enumMoniker != null)
                {
                    IMoniker[] monikers = new IMoniker[1];
                    IntPtr fetched = IntPtr.Zero;

                    while (enumMoniker.Next(1, monikers, fetched) == 0 && monikers[0] != null)
                    {
                        IMoniker moniker = monikers[0];
                        string friendlyName = string.Empty;
                        string devicePath = string.Empty;

                        try
                        {
                            Guid bagGuid = typeof(IPropertyBag).GUID;
                            moniker.BindToStorage(null!, null, ref bagGuid, out object bagObj);

                            if (bagObj is IPropertyBag propertyBag)
                            {
                                object val = string.Empty;
                                if (propertyBag.Read("FriendlyName", ref val, IntPtr.Zero) == 0 && val != null)
                                {
                                    friendlyName = val.ToString() ?? string.Empty;
                                }

                                object pathVal = string.Empty;
                                if (propertyBag.Read("DevicePath", ref pathVal, IntPtr.Zero) == 0 && pathVal != null)
                                {
                                    devicePath = pathVal.ToString() ?? string.Empty;
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            Marshal.ReleaseComObject(moniker);
                        }

                        if (!string.IsNullOrWhiteSpace(friendlyName) && !results.Exists(d => d.Name == friendlyName))
                        {
                            bool isSdi = IsSdiBroadcastDevice(friendlyName);
                            if (isSdi)
                            {
                                string prefix = direction == SdiPortDirection.Output ? "📡 [SDI OUT]" : "📡 [SDI IN]";
                                results.Add(new SdiDeviceInfo
                                {
                                    Name = friendlyName,
                                    DisplayLabel = $"{prefix} {friendlyName}",
                                    Direction = direction,
                                    IsPhysicalHardware = true,
                                    DevicePath = devicePath
                                });
                            }
                        }
                    }

                    Marshal.ReleaseComObject(enumMoniker);
                }

                if (devEnumObj is not null)
                {
                    Marshal.ReleaseComObject(devEnumObj);
                }
            }
            catch { }

            // Sort: physical SDI devices alphabetically
            results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            return results;
        }

        /// <summary>
        /// Cleans device labels from UI adornments (e.g. '[SDI IN]', '[PORT]', emojis)
        /// returning the pure device string for FFmpeg arguments.
        /// </summary>
        public static string CleanDeviceName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string clean = raw.Trim();
            // Remove common tags
            clean = clean.Replace("[SDI IN]", "")
                         .Replace("[SDI OUT]", "")
                         .Replace("[SDI HW]", "")
                         .Replace("[PORT]", "")
                         .Replace("[SDI/BROADCAST]", "")
                         .Replace("[CAPTURE/CAMERA]", "")
                         .Replace("📡", "")
                         .Replace("📷", "")
                         .Trim();

            int parenIndex = clean.IndexOf(" (System Video Device)", StringComparison.OrdinalIgnoreCase);
            if (parenIndex > 0)
            {
                clean = clean.Substring(0, parenIndex).Trim();
            }

            return clean;
        }

        public static bool IsSdiBroadcastDevice(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            return name.Contains("DeckLink", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Blackmagic", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("UltraStudio", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Intensity", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("AJA", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("KONA", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("SDI", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Magewell", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Pro Capture", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Bluefish", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Deltacast", StringComparison.OrdinalIgnoreCase);
        }
    }
}
