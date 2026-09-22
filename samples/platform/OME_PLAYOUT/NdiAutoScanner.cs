using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OME_PLAYOUT.Models;

namespace OME_PLAYOUT
{
    /// <summary>
    /// Engine quét tự động các luồng NDI trên mạng nội bộ LAN.
    /// Tự động phát hiện khi có nguồn NDI mới và đồng bộ vào danh sách hiển thị của OME_PLAYOUT.
    /// </summary>
    public sealed class NdiAutoScanner : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _timer;
        private bool _isScanning = false;
        private bool _isDisposed = false;

        // Preview Receiver cho luồng NDI đang được chọn/xem trước
        private NdiNativeReceiver? _activeReceiver;
        private string? _activeReceiverStreamName;
        private readonly byte[] _ndiFrameBuffer = new byte[320 * 180 * 4];

        public ObservableCollection<NdiPreviewItem> DiscoveredSources { get; } = new();

        public bool IsAutoScanEnabled { get; set; } = true;
        public int TotalCount => DiscoveredSources.Count;
        public int OnlineCount => DiscoveredSources.Count(x => x.IsOnline);

        public event Action<NdiPreviewItem>? SourceDiscovered;
        public event Action? ScanCompleted;

        public NdiAutoScanner(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(3500)
            };
            _timer.Tick += async (s, e) =>
            {
                if (IsAutoScanEnabled && !_isScanning)
                {
                    await ScanAsync();
                }
            };
        }

        public void Start()
        {
            _timer.Start();
            _ = ScanAsync();
        }

        public void Stop()
        {
            _timer.Stop();
            StopActiveReceiver();
        }

        public async Task ScanAsync()
        {
            if (_isScanning || _isDisposed) return;
            _isScanning = true;

            try
            {
                var names = await NdiFinderScanner.ScanSourcesAsync(1200);

                await _dispatcher.InvokeAsync(() =>
                {
                    var now = DateTime.UtcNow;

                    foreach (var name in names)
                    {
                        var existing = DiscoveredSources.FirstOrDefault(x => x.StreamName.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            existing.IsOnline = true;
                            existing.LastSeen = now;
                        }
                        else
                        {
                            var newItem = new NdiPreviewItem
                            {
                                StreamName = name,
                                IsOnline = true,
                                LastSeen = now,
                                ThumbnailBitmap = new WriteableBitmap(160, 90, 96, 96, PixelFormats.Bgr32, null)
                            };

                            InitNdiThumbnailDefault(newItem.ThumbnailBitmap, name);
                            DiscoveredSources.Add(newItem);
                            SourceDiscovered?.Invoke(newItem);
                        }
                    }

                    // Đánh dấu offline nếu không phản hồi sau 7 giây
                    foreach (var item in DiscoveredSources)
                    {
                        if (item.IsOnline && (now - item.LastSeen).TotalSeconds > 7.0)
                        {
                            item.IsOnline = false;
                        }
                    }

                    ScanCompleted?.Invoke();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[NdiAutoScanner] Scan exception: {ex.Message}");
            }
            finally
            {
                _isScanning = false;
            }
        }

        private static void InitNdiThumbnailDefault(WriteableBitmap bmp, string streamName)
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
                            // Tông xanh NDI Broadcast chuyên nghiệp
                            uint b = (uint)(35 + (x * 30 / 160));
                            uint g = (uint)(15 + (y * 15 / 90));
                            row[x] = unchecked((int)(0xFF000000u | (10u << 16) | (g << 8) | b));
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

        public void AttachLivePreview(NdiPreviewItem item)
        {
            if (_isDisposed || item == null) return;
            if (_activeReceiverStreamName == item.StreamName && _activeReceiver?.IsRunning == true) return;

            StopActiveReceiver();

            try
            {
                _activeReceiverStreamName = item.StreamName;
                _activeReceiver = new NdiNativeReceiver(item.StreamName);
                _activeReceiver.VideoFrameReceived += (bgraData, width, height, stride, fps) =>
                {
                    if (item.ThumbnailBitmap == null) return;

                    _dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            item.Format = $"{width}x{height} @ {fps:F2}fps";
                            var bmp = item.ThumbnailBitmap;
                            if (bmp != null)
                            {
                                bmp.Lock();
                                // Scale down to 160x90
                                unsafe
                                {
                                    fixed (byte* pSrc = bgraData)
                                    {
                                        int* pSrc32 = (int*)pSrc;
                                        int* pDst32 = (int*)bmp.BackBuffer;
                                        int dstStride32 = bmp.BackBufferStride / 4;
                                        int srcPitch32 = stride / 4;

                                        int stepY = Math.Max(1, height / 90);
                                        int stepX = Math.Max(1, width / 160);

                                        for (int y = 0; y < 90; y++)
                                        {
                                            int srcY = Math.Min(y * stepY, height - 1);
                                            int* srcRow = pSrc32 + srcY * srcPitch32;
                                            int* dstRow = pDst32 + y * dstStride32;

                                            for (int x = 0; x < 160; x++)
                                            {
                                                int srcX = Math.Min(x * stepX, width - 1);
                                                dstRow[x] = srcRow[srcX];
                                            }
                                        }
                                    }
                                }
                                bmp.AddDirtyRect(new Int32Rect(0, 0, 160, 90));
                                bmp.Unlock();
                            }
                        }
                        catch { }
                    }, DispatcherPriority.Render);
                };
                _activeReceiver.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[NdiAutoScanner] AttachLivePreview error: {ex.Message}");
            }
        }

        public void StopActiveReceiver()
        {
            if (_activeReceiver != null)
            {
                _activeReceiver.Stop();
                _activeReceiver.Dispose();
                _activeReceiver = null;
                _activeReceiverStreamName = null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _timer.Stop();
            StopActiveReceiver();
        }
    }
}
