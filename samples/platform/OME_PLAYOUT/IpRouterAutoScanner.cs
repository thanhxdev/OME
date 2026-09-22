using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OME_PLAYOUT.Models;
using OpenMedia.Platform.IPC;

namespace OME_PLAYOUT
{
    /// <summary>
    /// Engine quét tự động các cổng IP Video Matrix Router từ SRT DECODE hoặc WEBRTC DECODE.
    /// Tự động cập nhật danh sách và nạp live thumbnail cho từng kênh phát hiện được.
    /// </summary>
    public sealed class IpRouterAutoScanner : IDisposable
    {
        private readonly GpuMatrixCompositor _compositor;
        private readonly DispatcherTimer _scanTimer;
        private readonly byte[] _thumbScratch = new byte[160 * 90 * 4];
        private int _thumbSlotCursor = 0;
        private bool _isDisposed;

        public ObservableCollection<IpPreviewItem> DiscoveredSources { get; } = new();

        public bool IsAutoScanEnabled { get; set; } = true;
        public string FilterMode { get; set; } = "ALL"; // ALL, SRT, WEBRTC, ACTIVE

        public int ActiveCount => DiscoveredSources.Count(x => x.IsActive);
        public int TotalCount => DiscoveredSources.Count;

        public event Action<IpPreviewItem>? SourceDiscovered;
        public event Action? ScanCompleted;

        public IpRouterAutoScanner(GpuMatrixCompositor compositor)
        {
            _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));

            _scanTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _scanTimer.Tick += OnScanTimerTick;
        }

        public void Start()
        {
            _scanTimer.Start();
            ScanOnce();
        }

        public void Stop()
        {
            _scanTimer.Stop();
        }

        private void OnScanTimerTick(object? sender, EventArgs e)
        {
            if (IsAutoScanEnabled)
            {
                ScanOnce();
            }
        }

        public void ScanOnce()
        {
            if (_isDisposed) return;

            try
            {
                var discovered = _compositor.GetDiscoveredSources();

                foreach (var (slot, name, width, height, fps, isActive) in discovered)
                {
                    var existing = DiscoveredSources.FirstOrDefault(x => x.SlotIndex == slot);

                    string origin;
                    string originColor;
                    if (slot == 0)
                    {
                        origin = "SRT Decode";
                        originColor = "#EF4444";
                    }
                    else if (slot >= 1 && slot <= 10)
                    {
                        origin = "SRT Decode";
                        originColor = "#00E5FF";
                    }
                    else if (slot == 21)
                    {
                        origin = "WebRTC Decode";
                        originColor = "#C084FC";
                    }
                    else if (slot >= 11 && slot <= 20)
                    {
                        origin = "WebRTC Decode";
                        originColor = "#A855F7";
                    }
                    else
                    {
                        origin = "IP Router";
                        originColor = "#38BDF8";
                    }

                    string friendlyName = !string.IsNullOrWhiteSpace(name) && !name.StartsWith("Slot") 
                        ? name 
                        : (slot == 0 ? "SRT PGM MASTER" : (slot <= 10 ? $"SRT ISO CAM {slot:D2}" : (slot == 21 ? "WEBRTC PGM MASTER" : (slot <= 20 ? $"WEBRTC CAM {slot - 10:D2}" : $"IP ROUTER {slot:D2}"))));

                    if (existing != null)
                    {
                        // Cập nhật thông số
                        existing.Width = width;
                        existing.Height = height;
                        existing.Fps = fps;
                        existing.IsActive = isActive;
                        if (!string.IsNullOrWhiteSpace(friendlyName) && existing.DisplayName != friendlyName)
                        {
                            existing.DisplayName = friendlyName;
                        }

                        if (isActive)
                        {
                            existing.LastSeen = DateTime.UtcNow;
                        }

                        // Tự động nhận biết CAM # (PORT) đã gán cho slot này
                        int assignedPort = -1;
                        for (int p = 0; p <= 10; p++)
                        {
                            if (_compositor.GetPortRoute(p) == slot)
                            {
                                assignedPort = p;
                                break;
                            }
                        }
                        existing.AssignedPlayoutPort = assignedPort;
                    }
                    else
                    {
                        // Thêm slot (0..31) vào danh sách hiển thị
                        var newItem = new IpPreviewItem
                        {
                            SlotIndex = slot,
                            PortName = MatrixChannelConstants.GetTextureMmfName(slot),
                            DisplayName = friendlyName,
                            Origin = origin,
                            OriginBadgeColor = originColor,
                            Width = width,
                            Height = height,
                            Fps = fps > 0 ? fps : 59.94,
                            IsActive = isActive,
                            LastSeen = isActive ? DateTime.UtcNow : DateTime.MinValue,
                            ThumbnailBitmap = new WriteableBitmap(160, 90, 96, 96, PixelFormats.Bgr32, null)
                        };

                        // Khởi tạo bảng màu chờ cho thumbnail
                        InitThumbnailDefault(newItem.ThumbnailBitmap, slot);

                        // Tìm xem slot này đã được gán vào cổng nào của Playout chưa
                        for (int p = 0; p <= 10; p++)
                        {
                            if (_compositor.GetPortRoute(p) == slot)
                            {
                                newItem.AssignedPlayoutPort = p;
                                break;
                            }
                        }

                        DiscoveredSources.Add(newItem);
                        SourceDiscovered?.Invoke(newItem);
                    }
                }

                // Cập nhật trạng thái offline nếu mất heartbeat quá 3 giây
                var now = DateTime.UtcNow;
                foreach (var item in DiscoveredSources)
                {
                    if (item.IsActive && (now - item.LastSeen).TotalSeconds > 3.0)
                    {
                        item.IsActive = false;
                    }
                }

                ScanCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[IpRouterAutoScanner] Scan error: {ex.Message}");
            }
        }

        private static void InitThumbnailDefault(WriteableBitmap bmp, int slot)
        {
            try
            {
                bmp.Lock();
                unsafe
                {
                    int* p = (int*)bmp.BackBuffer;
                    int stride = bmp.BackBufferStride / 4;
                    for (int y = 0; y < 90; y++)
                    {
                        int* row = p + y * stride;
                        for (int x = 0; x < 160; x++)
                        {
                            // Gradient nền tối chuyên nghiệp
                            uint gray = (uint)(15 + (x * 10 / 160) + (y * 10 / 90));
                            row[x] = unchecked((int)(0xFF000000u | (gray << 16) | (gray << 8) | (gray + 8)));
                        }
                    }
                }
                bmp.AddDirtyRect(new Int32Rect(0, 0, 160, 90));
            }
            finally
            {
                bmp.Unlock();
            }
        }

        /// <summary>
        /// Cập nhật live thumbnail cho 1-2 slot active theo cơ chế luân phiên (Round-Robin) để tối ưu hóa CPU 0%.
        /// </summary>
        public void UpdateActiveThumbnailsRoundRobin()
        {
            if (_isDisposed || DiscoveredSources.Count == 0) return;

            var activeList = DiscoveredSources.Where(x => x.IsActive && x.ThumbnailBitmap != null).ToList();
            if (activeList.Count == 0) return;

            _thumbSlotCursor = (_thumbSlotCursor + 1) % activeList.Count;
            var item = activeList[_thumbSlotCursor];

            if (_compositor.ExtractSlotThumbnail(item.SlotIndex, _thumbScratch, 160, 90))
            {
                try
                {
                    var bmp = item.ThumbnailBitmap;
                    if (bmp != null)
                    {
                        bmp.Lock();
                        bmp.WritePixels(new Int32Rect(0, 0, 160, 90), _thumbScratch, 160 * 4, 0);
                        bmp.Unlock();
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _scanTimer.Stop();
        }
    }
}
