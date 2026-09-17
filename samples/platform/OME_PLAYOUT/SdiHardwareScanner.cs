using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;

namespace OME_PLAYOUT
{
    public sealed class SdiDeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DevicePath { get; set; } = string.Empty;
        public bool IsPhysicalHardware { get; set; }
        public string PortIdentifier { get; set; } = string.Empty;

        public string DisplayLabel => IsPhysicalHardware
            ? $"📡 [SDI HW] {Name}"
            : $"📡 [PORT] {Name}";

        public override string ToString() => DisplayLabel;
    }

    public static class SdiHardwareScanner
    {
        #region COM Interfaces for DirectShow Device Enumeration

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

        /// <summary>
        /// Returns instant default broadcast devices without performing unmanaged COM calls.
        /// Guaranteed 100% crash-free for fast application startup.
        /// </summary>
        public static List<SdiDeviceInfo> GetDefaultDevices()
        {
            var defaultPorts = new[]
            {
                "Blackmagic DeckLink 8K Pro (Port 1)",
                "Blackmagic DeckLink 8K Pro (Port 2)",
                "Blackmagic DeckLink 8K Pro (Port 3)",
                "Blackmagic DeckLink 8K Pro (Port 4)",
                "Blackmagic DeckLink Studio 4K (SDI Out)",
                "Blackmagic DeckLink Duo 2 (Port 1)",
                "Blackmagic DeckLink Duo 2 (Port 2)",
                "Blackmagic DeckLink Mini Monitor 4K",
                "AJA Kona 5 (SDI 1)",
                "AJA Kona 5 (SDI 2)",
                "Magewell Pro Capture SDI"
            };

            var results = new List<SdiDeviceInfo>();
            foreach (var port in defaultPorts)
            {
                results.Add(new SdiDeviceInfo
                {
                    Name = port,
                    DevicePath = string.Empty,
                    IsPhysicalHardware = false,
                    PortIdentifier = port
                });
            }
            return results;
        }

        /// <summary>
        /// Scans devices on a dedicated Single-Threaded Apartment (STA) thread
        /// to ensure DirectShow COM drivers do not throw access violations.
        /// </summary>
        public static Task<List<SdiDeviceInfo>> ScanDevicesAsync()
        {
            var tcs = new TaskCompletionSource<List<SdiDeviceInfo>>();
            var thread = new Thread(() =>
            {
                try
                {
                    var list = ScanDevices();
                    tcs.TrySetResult(list);
                }
                catch
                {
                    tcs.TrySetResult(GetDefaultDevices());
                }
            })
            {
                IsBackground = true
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        public static List<SdiDeviceInfo> ScanDevices()
        {
            var results = new List<SdiDeviceInfo>();

            try
            {
                Type? devEnumType = Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum);
                if (devEnumType != null)
                {
                    object? devEnumObj = Activator.CreateInstance(devEnumType);
                    if (devEnumObj is ICreateDevEnum devEnum)
                    {
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
                                    try { Marshal.ReleaseComObject(moniker); } catch { }
                                }

                                if (!string.IsNullOrWhiteSpace(friendlyName) && IsSdiBroadcastDevice(friendlyName))
                                {
                                    if (!results.Exists(d => d.Name == friendlyName))
                                    {
                                        results.Add(new SdiDeviceInfo
                                        {
                                            Name = friendlyName,
                                            DevicePath = devicePath,
                                            IsPhysicalHardware = true,
                                            PortIdentifier = friendlyName
                                        });
                                    }
                                }
                            }

                            try { Marshal.ReleaseComObject(enumMoniker); } catch { }
                        }

                        try { Marshal.ReleaseComObject(devEnum); } catch { }
                    }
                }
            }
            catch { }

            // Ensure standard DeckLink & broadcast ports are available even if cards are offline
            var defaultPorts = new[]
            {
                "Blackmagic DeckLink 8K Pro (Port 1)",
                "Blackmagic DeckLink 8K Pro (Port 2)",
                "Blackmagic DeckLink 8K Pro (Port 3)",
                "Blackmagic DeckLink 8K Pro (Port 4)",
                "Blackmagic DeckLink Studio 4K (SDI Out)",
                "Blackmagic DeckLink Duo 2 (Port 1)",
                "Blackmagic DeckLink Duo 2 (Port 2)",
                "Blackmagic DeckLink Mini Monitor 4K",
                "AJA Kona 5 (SDI 1)",
                "AJA Kona 5 (SDI 2)",
                "Magewell Pro Capture SDI"
            };

            foreach (var port in defaultPorts)
            {
                if (!results.Exists(d => d.Name.Equals(port, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new SdiDeviceInfo
                    {
                        Name = port,
                        DevicePath = string.Empty,
                        IsPhysicalHardware = false,
                        PortIdentifier = port
                    });
                }
            }

            // Physical hardware first, then alphabetical
            results.Sort((a, b) =>
            {
                if (a.IsPhysicalHardware && !b.IsPhysicalHardware) return -1;
                if (!a.IsPhysicalHardware && b.IsPhysicalHardware) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return results;
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
                   name.Contains("Bluefish", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Deltacast", StringComparison.OrdinalIgnoreCase);
        }
    }
}
