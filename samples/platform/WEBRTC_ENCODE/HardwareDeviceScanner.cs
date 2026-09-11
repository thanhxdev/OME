using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;

namespace WEBRTC_ENCODE
{
    #region COM Interfaces for DirectShow Device Enumeration

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyBag
    {
        [PreserveSig]
        int Read([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, Out, MarshalAs(UnmanagedType.Struct)] ref object pVar, [In] IntPtr pErrorLog);
        
        [PreserveSig]
        int Write([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, MarshalAs(UnmanagedType.Struct)] ref object pVar);
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator([In] ref Guid pType, [Out] out IEnumMoniker ppEnumMoniker, [In] int dwFlags);
    }

    #endregion

    public sealed class VideoDeviceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DevicePath { get; set; } = string.Empty;
        public bool IsSdiHardware { get; set; }

        public string DisplayLabel => IsSdiHardware
            ? $"📡 [SDI/BROADCAST] {Name}"
            : $"📷 [CAPTURE/CAMERA] {Name}";

        public override string ToString() => DisplayLabel;
    }

    public static class HardwareDeviceScanner
    {
        private static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");

        public static Task<List<VideoDeviceInfo>> ScanDevicesAsync()
        {
            return Task.Run(() => ScanDevices());
        }

        public static List<VideoDeviceInfo> ScanDevices()
        {
            var results = new List<VideoDeviceInfo>();

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
                        catch
                        {
                            // Continue on property read error
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(moniker);
                        }

                        if (!string.IsNullOrWhiteSpace(friendlyName) && !results.Exists(d => d.Name == friendlyName))
                        {
                            bool isSdi = IsDedicatedSdiDevice(friendlyName);
                            results.Add(new VideoDeviceInfo
                            {
                                Name = friendlyName,
                                DevicePath = devicePath,
                                IsSdiHardware = isSdi
                            });
                        }
                    }

                    Marshal.ReleaseComObject(enumMoniker);
                }

                Marshal.ReleaseComObject(devEnum);
            }
            catch
            {
                // Fallback handled cleanly
            }

            // Sắp xếp: Thiết bị phần cứng chuyên dụng SDI lên trước, sau đó tới các thiết bị Capture/Webcam
            results.Sort((a, b) =>
            {
                if (a.IsSdiHardware && !b.IsSdiHardware) return -1;
                if (!a.IsSdiHardware && b.IsSdiHardware) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return results;
        }

        public static bool IsDedicatedSdiDevice(string name)
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

    public sealed class GpuEncoderOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayTitle => DisplayName;
        public string CodecKey { get; set; } = string.Empty; // "QSV", "NVENC", "AMF", "MF", "CPU"
        public bool IsHardwareAccelerated { get; set; } = true;
        public bool IsPreferred { get; set; } = false;
        public string Description { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    public static class GpuHardwareScanner
    {
        public static List<GpuEncoderOption> ScanAvailableEncoders()
        {
            var options = new List<GpuEncoderOption>();
            var activeGpus = DetectActiveGpus();

            bool hasNvidia = activeGpus.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || g.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || g.Contains("RTX", StringComparison.OrdinalIgnoreCase) || g.Contains("Quadro", StringComparison.OrdinalIgnoreCase));
            bool hasIntel = activeGpus.Any(g => g.Contains("Intel", StringComparison.OrdinalIgnoreCase) || g.Contains("Arc", StringComparison.OrdinalIgnoreCase) || g.Contains("Iris", StringComparison.OrdinalIgnoreCase) || g.Contains("UHD", StringComparison.OrdinalIgnoreCase));
            bool hasAmd = activeGpus.Any(g => g.Contains("AMD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon", StringComparison.OrdinalIgnoreCase));

            // Ưu tiên các encoder phần cứng thực tế đang hiện diện
            if (hasIntel)
            {
                options.Add(new GpuEncoderOption
                {
                    DisplayName = "⚡ Intel QuickSync Video (QSV Hardware)",
                    CodecKey = "QSV",
                    IsHardwareAccelerated = true,
                    IsPreferred = true,
                    Description = "Intel Arc / Iris Xe / Core GPU Hardware Acceleration"
                });
            }

            if (hasNvidia)
            {
                options.Add(new GpuEncoderOption
                {
                    DisplayName = "⚡ NVIDIA NVENC (Hardware Acceleration)",
                    CodecKey = "NVENC",
                    IsHardwareAccelerated = true,
                    IsPreferred = !hasIntel,
                    Description = "NVIDIA GeForce / RTX NVENC Hardware Acceleration"
                });
            }

            if (hasAmd)
            {
                options.Add(new GpuEncoderOption
                {
                    DisplayName = "⚡ AMD AMF Video Engine (Hardware)",
                    CodecKey = "AMF",
                    IsHardwareAccelerated = true,
                    IsPreferred = !hasIntel && !hasNvidia,
                    Description = "AMD Radeon Advanced Media Framework Hardware Acceleration"
                });
            }

            // Windows Media Foundation Hardware / MFT (hỗ trợ mọi Windows PC có GPU DirectX 11)
            options.Add(new GpuEncoderOption
            {
                DisplayName = "🛡️ Windows Media Foundation (MF Hardware/D3D11)",
                CodecKey = "MF",
                IsHardwareAccelerated = true,
                Description = "Windows MFT Universal Hardware Encoder"
            });

            // Software CPU Fallback
            options.Add(new GpuEncoderOption
            {
                DisplayName = "💻 Software (CPU libx264/libx265 Ultrafast)",
                CodecKey = "CPU",
                IsHardwareAccelerated = false,
                Description = "Universal Software CPU Encoder (Zero Latency)"
            });

            if (options.Count > 0 && !options.Any(o => o.IsPreferred))
            {
                options[0].IsPreferred = true;
            }

            return options;
        }

        public static List<string> DetectActiveGpus()
        {
            var gpus = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_VideoController | Where-Object { $_.Status -eq 'OK' } | Select-Object -ExpandProperty Name\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(1500);

                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!gpus.Contains(trimmed)) gpus.Add(trimmed);
                        }
                    }
                }
            }
            catch { }

            // Fallback sang Registry nếu PowerShell bị hạn chế
            if (gpus.Count == 0)
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                    if (key != null)
                    {
                        foreach (var sub in key.GetSubKeyNames())
                        {
                            if (sub.StartsWith("000"))
                            {
                                using var sk = key.OpenSubKey(sub);
                                var desc = sk?.GetValue("DriverDesc") as string;
                                if (!string.IsNullOrEmpty(desc) && !desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!gpus.Contains(desc)) gpus.Add(desc);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return gpus;
        }
    }

    #region SDI / DirectShow Hardware Video Capture Loop

    public sealed class SdiDeviceCapture : IDisposable
    {
        private Process? _captureProcess;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private bool _isDisposed;

        public string DeviceName { get; }
        public bool IsRunning => _captureProcess != null && !_captureProcess.HasExited && _cts != null && !_cts.IsCancellationRequested;

        public event Action<byte[], int, int, int, double>? VideoFrameReceived;
        public event Action<string, string>? LogRequested;

        public SdiDeviceCapture(string deviceName)
        {
            DeviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
        }

        public bool Start(int width = 1920, int height = 1080, double fps = 30.0)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(SdiDeviceCapture));
            if (IsRunning) return true;

            if (string.IsNullOrWhiteSpace(DeviceName) ||
                DeviceName.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase) ||
                DeviceName.Contains("Chưa kết nối", StringComparison.OrdinalIgnoreCase))
            {
                LogRequested?.Invoke("[WARN]", "Chưa chọn thiết bị phần cứng video input hợp lệ.");
                return false;
            }

            try
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                // Chuẩn hóa tên thiết bị cho DirectShow
                string cleanDeviceName = DeviceName;
                int tagIndex = cleanDeviceName.IndexOf("]");
                if (tagIndex >= 0 && tagIndex < cleanDeviceName.Length - 1)
                {
                    cleanDeviceName = cleanDeviceName.Substring(tagIndex + 1).Trim();
                }

                int parenIndex = cleanDeviceName.IndexOf(" (System Video Device)");
                if (parenIndex > 0)
                {
                    cleanDeviceName = cleanDeviceName.Substring(0, parenIndex).Trim();
                }

                // Chạy FFmpeg background process đọc DirectShow frame chuyển đổi sang BGRA raw byte stream
                int intFps = (int)Math.Round(fps > 0 ? fps : 30.0);
                string args = $"-hide_banner -loglevel error -f dshow -rtbufsize 100M -i video=\"{cleanDeviceName}\" -pix_fmt bgra -s {width}x{height} -r {intFps} -f rawvideo pipe:1";

                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _captureProcess = Process.Start(psi);
                if (_captureProcess == null || _captureProcess.HasExited)
                {
                    LogRequested?.Invoke("[WARN]", $"Không thể khởi chạy bộ bắt tín hiệu DirectShow cho: {cleanDeviceName}");
                    return false;
                }

                var proc = _captureProcess;
                _readTask = Task.Run(async () =>
                {
                    int stride = width * 4;
                    int frameSize = height * stride;
                    byte[] frameBuffer = new byte[frameSize];

                    try
                    {
                        using var stdout = proc.StandardOutput.BaseStream;

                        while (!token.IsCancellationRequested && !proc.HasExited)
                        {
                            int totalRead = 0;
                            while (totalRead < frameSize)
                            {
                                int read = await stdout.ReadAsync(frameBuffer.AsMemory(totalRead, frameSize - totalRead), token);
                                if (read <= 0) break;
                                totalRead += read;
                            }

                            if (totalRead == frameSize)
                            {
                                byte[] frameCopy = new byte[frameSize];
                                Buffer.BlockCopy(frameBuffer, 0, frameCopy, 0, frameSize);
                                VideoFrameReceived?.Invoke(frameCopy, width, height, stride, fps);
                            }
                            else
                            {
                                await Task.Delay(5, token);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        LogRequested?.Invoke("[WARN]", $"Luồng bắt frame SDI/DirectShow: {ex.Message}");
                    }
                }, token);

                LogRequested?.Invoke("[INFO]", $"✅ [SDI] Đã kết nối và kích hoạt capture tín hiệu phần cứng: {cleanDeviceName} ({width}x{height} @ {fps:F2} fps)");
                return true;
            }
            catch (Exception ex)
            {
                LogRequested?.Invoke("[WARN]", $"Lỗi khởi động bắt hình SDI/Capture: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                try
                {
                    _readTask?.Wait(250);
                }
                catch { }
                _cts.Dispose();
                _cts = null;
            }

            if (_captureProcess != null && !_captureProcess.HasExited)
            {
                try
                {
                    _captureProcess.Kill(true);
                }
                catch { }
                _captureProcess.Dispose();
                _captureProcess = null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }

    #endregion
}
