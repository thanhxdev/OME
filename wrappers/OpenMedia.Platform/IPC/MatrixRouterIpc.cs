using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace OpenMedia.Platform.IPC
{
    /// <summary>
    /// Channel identification in the 11-channel IP Video Matrix Router.
    /// Channel 0 = PGM Master (switched feed).
    /// Channels 1..10 = ISO Cameras 1..10 (clean raw feeds).
    /// </summary>
    public static class MatrixChannelConstants
    {
        public const int MaxIsoChannels = 31;
        public const int TotalMatrixChannels = 32; // 1 PGM (Port 0) + 31 ISO/Source Slots (Port 1..31)

        public const int PgmChannelIndex = 0;
        public const int Iso1ChannelIndex = 1;

        // Named IPC Resources (Session-Local for reliable cross-process IPC without requiring Admin privileges)
        public const string MasterMetadataMmfName = "OME_MATRIX_ROUTER_METADATA";
        public const string AudioMmfName = "OME_MATRIX_AUDIO_MMF";
        public const string AudioSyncEventName = "OME_MATRIX_AUDIO_SYNC_EVENT";

        public const string PgmTextureMmfName = "OME_TEX_PGM_MASTER";
        public const string IsoTextureMmfPrefix = "OME_TEX_ISO_CAM_"; // + 01..31

        public static string GetTextureMmfName(int channelIndex)
        {
            if (channelIndex == 0) return PgmTextureMmfName;
            return $"{IsoTextureMmfPrefix}{channelIndex:D2}";
        }
    }

    /// <summary>
    /// Metadata descriptor for a single video channel in the matrix router.
    /// Blittable struct stored in MemoryMappedFile for zero-overhead inter-process discovery.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public unsafe struct MatrixVideoChannelInfo
    {
        public long SharedHandle;       // DXGI Shared Resource NT/KMT Handle (IntPtr as int64)
        public int Width;               // Frame width (e.g., 1920)
        public int Height;              // Frame height (e.g., 1080)
        public int Format;              // DXGI Format (e.g., Format.B8G8R8A8_UNorm = 87)
        public long FrameIndex;         // Monotonically increasing frame counter
        public long TimestampUtcTicks;  // Frame presentation timestamp in DateTime.UtcNow.Ticks
        public int IsActive;            // 1 if active and streaming, 0 if standby/offline
        public double Fps;              // Current FPS rate (e.g. 59.94)
        public fixed byte SourceNameBytes[32]; // Human-readable friendly source name (UTF-8, max 31 chars)
        public int Reserved;            // Padding for 64-bit alignment

        public string SourceName
        {
            get
            {
                fixed (byte* p = SourceNameBytes)
                {
                    int len = 0;
                    while (len < 32 && p[len] != 0) len++;
                    return len == 0 ? "" : System.Text.Encoding.UTF8.GetString(p, len);
                }
            }
            set
            {
                fixed (byte* p = SourceNameBytes)
                {
                    for (int i = 0; i < 32; i++) p[i] = 0;
                    if (!string.IsNullOrEmpty(value))
                    {
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
                        int count = Math.Min(bytes.Length, 31);
                        Marshal.Copy(bytes, 0, (IntPtr)p, count);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Master Router Table containing metadata for all 11 channels.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct MatrixRouterMetadataTable
    {
        public int Magic;               // 0x4D4F4D52 ('OMMR' - OpenMedia Matrix Router)
        public int Version;             // 1
        public int ChannelCount;        // 11
        public int PublisherProcessId;  // Process ID of the active publisher
        public long LastHeartbeatTicks; // Heartbeat to detect publisher crash/restart

        // 11 channel slots: Index 0 = PGM Master, Index 1..10 = Cam 1..10 ISO
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MatrixChannelConstants.TotalMatrixChannels)]
        public MatrixVideoChannelInfo[] Channels;
    }

    /// <summary>
    /// Ring buffer header for a single audio channel in the Audio MMF.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct MatrixAudioChannelHeader
    {
        public int SampleRate;          // 48000
        public int Channels;            // 2 (Stereo)
        public int BitsPerSample;       // 16
        public int BufferCapacityBytes; // Capacity of ring buffer (e.g., 192000 bytes = 1 sec)
        public int WriteOffset;         // Current write pointer in ring buffer
        public int ReadOffset;          // Current read pointer
        public long TotalBytesWritten;  // Total bytes written since start
        public long TimestampUtcTicks;  // Timestamp of latest audio chunk
        public int IsActive;            // 1 if active, 0 if silent/inactive
        public int Reserved;
    }

    /// <summary>
    /// Publisher side of the IP Video Matrix Router.
    /// Runs inside WEBRTC_DECODE or SRT_DECODE to create 11 D3D11 Shared Textures and MMF audio ring buffers.
    /// </summary>
    public sealed class MatrixRouterPublisher : IDisposable
    {
        private readonly object _syncLock = new();
        private bool _isDisposed;

        // D3D11 Subsystem
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private readonly ID3D11Texture2D?[] _textures = new ID3D11Texture2D?[MatrixChannelConstants.TotalMatrixChannels];
        private readonly IntPtr[] _sharedHandles = new IntPtr[MatrixChannelConstants.TotalMatrixChannels];
        private readonly int[] _widths = new int[MatrixChannelConstants.TotalMatrixChannels];
        private readonly int[] _heights = new int[MatrixChannelConstants.TotalMatrixChannels];
        private readonly long[] _frameCounters = new long[MatrixChannelConstants.TotalMatrixChannels];

        // Memory Mapped Files for Video Metadata
        private MemoryMappedFile? _metadataMmf;
        private MemoryMappedViewAccessor? _metadataAccessor;
        private readonly MemoryMappedFile?[] _channelMmfs = new MemoryMappedFile?[MatrixChannelConstants.TotalMatrixChannels];
        private readonly MemoryMappedViewAccessor?[] _channelAccessors = new MemoryMappedViewAccessor?[MatrixChannelConstants.TotalMatrixChannels];
        public int SlotBaseIndex { get; set; } = 0;
        private readonly string[] _channelSourceNames = new string[MatrixChannelConstants.TotalMatrixChannels];

        // Audio IPC
        public const int AudioRingBufferSizePerChannel = 192000; // 1 sec at 48kHz Stereo 16-bit (192 KB)
        private MemoryMappedFile? _audioMmf;
        private MemoryMappedViewAccessor? _audioAccessor;
        private EventWaitHandle? _audioSyncEvent;
        private readonly int _audioHeaderSize;
        private readonly int _audioSlotTotalSize;

        public bool IsInitialized { get; private set; }
        public string? LastError { get; private set; }

        public MatrixRouterPublisher()
        {
            _audioHeaderSize = Marshal.SizeOf<MatrixAudioChannelHeader>();
            _audioSlotTotalSize = _audioHeaderSize + AudioRingBufferSizePerChannel;
        }

        public bool Initialize(int defaultWidth = 1920, int defaultHeight = 1080)
        {
            lock (_syncLock)
            {
                if (IsInitialized) return true;

                try
                {
                    // 1. Initialize Direct3D11 Device
                    var result = D3D11.D3D11CreateDevice(
                        adapter: null!,
                        DriverType.Hardware,
                        DeviceCreationFlags.BgraSupport,
                        featureLevels: new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                        out _device,
                        out _context);

                    if (result.Failure || _device == null || _context == null)
                    {
                        D3D11.D3D11CreateDevice(
                            adapter: null!,
                            DriverType.Warp,
                            DeviceCreationFlags.BgraSupport,
                            featureLevels: new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                            out _device,
                            out _context);
                    }

                    if (_device == null || _context == null)
                    {
                        return false;
                    }

                    // 2. Create 11 D3D11 Shared Textures on VRAM
                    for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                    {
                        CreateOrResizeTexture(i, defaultWidth, defaultHeight);
                    }

                    // 3. Initialize Metadata MMF
                    int metadataSize = Marshal.SizeOf<MatrixRouterMetadataTable>() + 512;
                    _metadataMmf = MemoryMappedFile.CreateOrOpen(MatrixChannelConstants.MasterMetadataMmfName, metadataSize, MemoryMappedFileAccess.ReadWrite);
                    _metadataAccessor = _metadataMmf.CreateViewAccessor(0, metadataSize, MemoryMappedFileAccess.ReadWrite);

                    // Initialize individual channel MMFs and persistent accessors
                    for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                    {
                        string mmfName = MatrixChannelConstants.GetTextureMmfName(i);
                        int chanSize = Marshal.SizeOf<MatrixVideoChannelInfo>();
                        _channelMmfs[i] = MemoryMappedFile.CreateOrOpen(mmfName, chanSize, MemoryMappedFileAccess.ReadWrite);
                        _channelAccessors[i] = _channelMmfs[i].CreateViewAccessor(0, chanSize, MemoryMappedFileAccess.ReadWrite);
                        UpdateChannelMmf(i);
                    }

                    // 4. Initialize Audio IPC MMF and Event
                    int totalAudioSize = _audioSlotTotalSize * MatrixChannelConstants.TotalMatrixChannels;
                    _audioMmf = MemoryMappedFile.CreateOrOpen(MatrixChannelConstants.AudioMmfName, totalAudioSize, MemoryMappedFileAccess.ReadWrite);
                    _audioAccessor = _audioMmf.CreateViewAccessor(0, totalAudioSize, MemoryMappedFileAccess.ReadWrite);

                    // Reset audio headers
                    for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                    {
                        var hdr = new MatrixAudioChannelHeader
                        {
                            SampleRate = 48000,
                            Channels = 2,
                            BitsPerSample = 16,
                            BufferCapacityBytes = AudioRingBufferSizePerChannel,
                            WriteOffset = 0,
                            ReadOffset = 0,
                            TotalBytesWritten = 0,
                            TimestampUtcTicks = DateTime.UtcNow.Ticks,
                            IsActive = 0
                        };
                        WriteAudioHeader(i, ref hdr);
                    }

                    _audioSyncEvent = new EventWaitHandle(false, EventResetMode.AutoReset, MatrixChannelConstants.AudioSyncEventName);

                    UpdateMasterMetadataTable();

                    IsInitialized = true;
                    return true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    Console.WriteLine($"[MatrixRouterPublisher] Initialize failed: {ex.Message}");
                    return false;
                }
            }
        }

        private void CreateOrResizeTexture(int channelIndex, int width, int height)
        {
            if (_device == null) return;

            _textures[channelIndex]?.Dispose();
            _textures[channelIndex] = null;
            _sharedHandles[channelIndex] = IntPtr.Zero;

            var desc = new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                CPUAccessFlags = CpuAccessFlags.None,
                MiscFlags = ResourceOptionFlags.Shared
            };

            var texture = _device.CreateTexture2D(desc);
            _textures[channelIndex] = texture;
            _widths[channelIndex] = width;
            _heights[channelIndex] = height;

            using var dxgiResource = texture.QueryInterface<IDXGIResource>();
            _sharedHandles[channelIndex] = dxgiResource.SharedHandle;
        }

        #region Video Feed Methods

        /// <summary>
        /// Updates the PGM Master Shared Texture (Port 0) with a newly switched broadcast frame.
        /// </summary>
        public void UpdateMasterVideo(byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            UpdateChannelVideo(MatrixChannelConstants.PgmChannelIndex, bgraBytes, width, height, fps);
        }

        /// <summary>
        /// Updates one of the ISO Camera Shared Textures with a raw decoded frame and friendly source name.
        /// </summary>
        public void UpdateIsoVideo(int camIndex, string sourceName, byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            if (camIndex < 0) return;
            int port = SlotBaseIndex > 0 ? (SlotBaseIndex + camIndex) : (camIndex + 1);
            if (port >= MatrixChannelConstants.TotalMatrixChannels) return;
            UpdateChannelVideo(port, sourceName, bgraBytes, width, height, fps);
        }

        public void UpdateIsoVideo(int camIndex, byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            UpdateIsoVideo(camIndex, "", bgraBytes, width, height, fps);
        }

        public void UpdateChannelVideo(int port, byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            UpdateChannelVideo(port, "", bgraBytes, width, height, fps);
        }

        public void UpdateChannelVideo(int port, string sourceName, byte[] bgraBytes, int width, int height, double fps = 59.94)
        {
            if (!IsInitialized || _isDisposed || _context == null || _device == null) return;
            if (port < 0 || port >= MatrixChannelConstants.TotalMatrixChannels) return;
            if (bgraBytes == null || bgraBytes.Length < width * height * 4) return;

            if (!string.IsNullOrEmpty(sourceName))
            {
                _channelSourceNames[port] = sourceName;
            }

            lock (_syncLock)
            {
                // Verify texture dimensions
                if (_textures[port] == null || _widths[port] != width || _heights[port] != height)
                {
                    CreateOrResizeTexture(port, width, height);
                    UpdateChannelMmf(port, _channelSourceNames[port], fps);
                    UpdateMasterMetadataTable();
                }

                var texture = _textures[port];
                if (texture == null) return;

                int rowPitch = width * 4;

                unsafe
                {
                    fixed (byte* pData = bgraBytes)
                    {
                        _context.UpdateSubresource(texture, 0, null, (IntPtr)pData, (uint)rowPitch, 0);
                    }
                }

                _context.Flush();

                _frameCounters[port]++;

                // Update channel MMF metadata
                UpdateChannelMmf(port, _channelSourceNames[port], fps);
            }
        }

        private void UpdateChannelMmf(int port, string sourceName = "", double fps = 59.94)
        {
            var info = new MatrixVideoChannelInfo
            {
                SharedHandle = _sharedHandles[port].ToInt64(),
                Width = _widths[port],
                Height = _heights[port],
                Format = (int)Format.B8G8R8A8_UNorm,
                FrameIndex = _frameCounters[port],
                TimestampUtcTicks = DateTime.UtcNow.Ticks,
                IsActive = _sharedHandles[port] != IntPtr.Zero ? 1 : 0,
                Fps = fps
            };
            if (!string.IsNullOrEmpty(sourceName))
            {
                info.SourceName = sourceName;
            }
            else if (!string.IsNullOrEmpty(_channelSourceNames[port]))
            {
                info.SourceName = _channelSourceNames[port];
            }

            var accessor = _channelAccessors[port];
            if (accessor != null)
            {
                try
                {
                    accessor.Write(0, ref info);
                    accessor.Flush();
                }
                catch { }
            }
        }

        private void UpdateMasterMetadataTable()
        {
            if (_metadataAccessor == null) return;

            try
            {
                var table = new MatrixRouterMetadataTable
                {
                    Magic = 0x4D4F4D52,
                    Version = 1,
                    ChannelCount = MatrixChannelConstants.TotalMatrixChannels,
                    PublisherProcessId = Environment.ProcessId,
                    LastHeartbeatTicks = DateTime.UtcNow.Ticks,
                    Channels = new MatrixVideoChannelInfo[MatrixChannelConstants.TotalMatrixChannels]
                };

                for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                {
                    var chanInfo = new MatrixVideoChannelInfo
                    {
                        SharedHandle = _sharedHandles[i].ToInt64(),
                        Width = _widths[i],
                        Height = _heights[i],
                        Format = (int)Format.B8G8R8A8_UNorm,
                        FrameIndex = _frameCounters[i],
                        TimestampUtcTicks = DateTime.UtcNow.Ticks,
                        IsActive = _sharedHandles[i] != IntPtr.Zero ? 1 : 0,
                        Fps = 59.94
                    };
                    if (!string.IsNullOrEmpty(_channelSourceNames[i]))
                    {
                        chanInfo.SourceName = _channelSourceNames[i];
                    }
                    table.Channels[i] = chanInfo;
                }

                _metadataAccessor.Write(0, ref table);
            }
            catch { }
        }

        #endregion

        #region Audio Feed Methods

        /// <summary>
        /// Updates the Master Program Audio (Port 0) in the IPC Audio MMF.
        /// </summary>
        public void UpdateMasterAudio(byte[] pcmBytes, int length)
        {
            UpdateChannelAudio(MatrixChannelConstants.PgmChannelIndex, pcmBytes, length);
        }

        /// <summary>
        /// Updates one of the ISO Camera Audios in the IPC Audio MMF.
        /// </summary>
        public void UpdateIsoAudio(int camIndex, byte[] pcmBytes, int length)
        {
            if (camIndex < 0) return;
            int port = SlotBaseIndex > 0 ? (SlotBaseIndex + camIndex) : (camIndex + 1);
            if (port >= MatrixChannelConstants.TotalMatrixChannels) return;
            UpdateChannelAudio(port, pcmBytes, length);
        }

        public void UpdateChannelAudio(int port, byte[] pcmBytes, int length)
        {
            if (!IsInitialized || _isDisposed || _audioAccessor == null) return;
            if (port < 0 || port >= MatrixChannelConstants.TotalMatrixChannels) return;
            if (pcmBytes == null || length <= 0) return;

            try
            {
                int slotBaseOffset = port * _audioSlotTotalSize;
                int bufferBaseOffset = slotBaseOffset + _audioHeaderSize;

                MatrixAudioChannelHeader hdr;
                ReadAudioHeader(port, out hdr);

                int writeOffset = hdr.WriteOffset;
                int toWrite = Math.Min(length, AudioRingBufferSizePerChannel);

                // Write into ring buffer with potential wrap-around
                int firstChunk = Math.Min(toWrite, AudioRingBufferSizePerChannel - writeOffset);
                _audioAccessor.WriteArray(bufferBaseOffset + writeOffset, pcmBytes, 0, firstChunk);

                if (firstChunk < toWrite)
                {
                    int secondChunk = toWrite - firstChunk;
                    _audioAccessor.WriteArray(bufferBaseOffset, pcmBytes, firstChunk, secondChunk);
                    writeOffset = secondChunk;
                }
                else
                {
                    writeOffset = (writeOffset + firstChunk) % AudioRingBufferSizePerChannel;
                }

                hdr.WriteOffset = writeOffset;
                hdr.TotalBytesWritten += toWrite;
                hdr.TimestampUtcTicks = DateTime.UtcNow.Ticks;
                hdr.IsActive = 1;

                WriteAudioHeader(port, ref hdr);

                _audioSyncEvent?.Set();
            }
            catch { }
        }

        private void ReadAudioHeader(int port, out MatrixAudioChannelHeader header)
        {
            int offset = port * _audioSlotTotalSize;
            _audioAccessor!.Read(offset, out header);
        }

        private void WriteAudioHeader(int port, ref MatrixAudioChannelHeader header)
        {
            int offset = port * _audioSlotTotalSize;
            _audioAccessor!.Write(offset, ref header);
        }

        #endregion

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                {
                    _textures[i]?.Dispose();
                    _textures[i] = null;
                    _channelAccessors[i]?.Dispose();
                    _channelAccessors[i] = null;
                    _channelMmfs[i]?.Dispose();
                    _channelMmfs[i] = null;
                }

                _context?.Dispose();
                _device?.Dispose();

                _metadataAccessor?.Dispose();
                _metadataMmf?.Dispose();

                _audioAccessor?.Dispose();
                _audioMmf?.Dispose();
                _audioSyncEvent?.Dispose();

                IsInitialized = false;
            }
        }
    }

    /// <summary>
    /// Subscriber side of the IP Video Matrix Router.
    /// Runs inside OME_PLAYOUT to consume shared textures and synchronized audio streams.
    /// </summary>
    public sealed class MatrixRouterSubscriber : IDisposable
    {
        private readonly object _syncLock = new();
        private bool _isDisposed;

        private ID3D11Device? _device;
        private readonly ID3D11Texture2D?[] _openedTextures = new ID3D11Texture2D?[MatrixChannelConstants.TotalMatrixChannels];
        private readonly IntPtr[] _cachedHandles = new IntPtr[MatrixChannelConstants.TotalMatrixChannels];
        private readonly MatrixVideoChannelInfo[] _channelInfos = new MatrixVideoChannelInfo[MatrixChannelConstants.TotalMatrixChannels];

        private MemoryMappedFile? _metadataMmf;
        private MemoryMappedViewAccessor? _metadataAccessor;
        private readonly MemoryMappedFile?[] _channelMmfs = new MemoryMappedFile?[MatrixChannelConstants.TotalMatrixChannels];
        private readonly MemoryMappedViewAccessor?[] _channelAccessors = new MemoryMappedViewAccessor?[MatrixChannelConstants.TotalMatrixChannels];
        private readonly long[] _lastMmfOpenAttemptTicks = new long[MatrixChannelConstants.TotalMatrixChannels];

        private MemoryMappedFile? _audioMmf;
        private MemoryMappedViewAccessor? _audioAccessor;
        private EventWaitHandle? _audioSyncEvent;
        private Thread? _audioReaderThread;
        private volatile bool _audioReaderRunning;

        private readonly int _audioHeaderSize;
        private readonly int _audioSlotTotalSize;
        private readonly long[] _lastReadTotalAudio = new long[MatrixChannelConstants.TotalMatrixChannels];

        public event Action<int, MatrixVideoChannelInfo>? ChannelMetadataUpdated;
        public event Action<int, byte[], int>? ChannelAudioReceived; // port, pcmBytes, length

        public MatrixRouterSubscriber(ID3D11Device? device = null)
        {
            _device = device;
            _audioHeaderSize = Marshal.SizeOf<MatrixAudioChannelHeader>();
            _audioSlotTotalSize = _audioHeaderSize + MatrixRouterPublisher.AudioRingBufferSizePerChannel;
        }

        private bool TryOpenChannelMmf(int port)
        {
            if (port < 0 || port >= MatrixChannelConstants.TotalMatrixChannels) return false;
            if (_channelAccessors[port] != null) return true;

            long nowTicks = Environment.TickCount64;
            if (nowTicks - _lastMmfOpenAttemptTicks[port] < 500) return false;
            _lastMmfOpenAttemptTicks[port] = nowTicks;

            try
            {
                string mmfName = MatrixChannelConstants.GetTextureMmfName(port);
                _channelMmfs[port] = MemoryMappedFile.OpenExisting(mmfName, MemoryMappedFileRights.Read);
                _channelAccessors[port] = _channelMmfs[port].CreateViewAccessor(0, Marshal.SizeOf<MatrixVideoChannelInfo>(), MemoryMappedFileAccess.Read);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Connect(ID3D11Device? device = null)
        {
            lock (_syncLock)
            {
                if (device != null) _device = device;

                try
                {
                    if (_device == null)
                    {
                        D3D11.D3D11CreateDevice(
                            adapter: null!,
                            DriverType.Hardware,
                            DeviceCreationFlags.BgraSupport,
                            featureLevels: new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                            out _device,
                            out ID3D11DeviceContext? _);
                    }

                    // Try opening metadata MMF
                    try
                    {
                        _metadataMmf = MemoryMappedFile.OpenExisting(MatrixChannelConstants.MasterMetadataMmfName, MemoryMappedFileRights.Read);
                        _metadataAccessor = _metadataMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    }
                    catch { }

                    // Open individual channel MMFs
                    for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                    {
                        TryOpenChannelMmf(i);
                    }

                    // Open Audio IPC
                    try
                    {
                        _audioMmf = MemoryMappedFile.OpenExisting(MatrixChannelConstants.AudioMmfName, MemoryMappedFileRights.Read);
                        _audioAccessor = _audioMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                        _audioSyncEvent = EventWaitHandle.OpenExisting(MatrixChannelConstants.AudioSyncEventName);

                        _audioReaderRunning = true;
                        _audioReaderThread = new Thread(AudioReaderLoop)
                        {
                            IsBackground = true,
                            Name = "MatrixRouterAudioSubscriber"
                        };
                        _audioReaderThread.Start();
                    }
                    catch { }

                    RefreshAllChannels();
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MatrixRouterSubscriber] Connect error: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if a router publisher is actively online and broadcasting.
        /// </summary>
        public bool IsPublisherActive()
        {
            try
            {
                if (_metadataAccessor == null)
                {
                    _metadataMmf = MemoryMappedFile.OpenExisting(MatrixChannelConstants.MasterMetadataMmfName, MemoryMappedFileRights.Read);
                    _metadataAccessor = _metadataMmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                }

                _metadataAccessor.Read(0, out MatrixRouterMetadataTable table);
                if (table.Magic != 0x4D4F4D52) return false;

                long ticksSinceHeartbeat = DateTime.UtcNow.Ticks - table.LastHeartbeatTicks;
                return TimeSpan.FromTicks(ticksSinceHeartbeat).TotalSeconds < 3.0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads the latest channel info from MMF and opens/reopens the shared D3D11 texture if handle changed.
        /// </summary>
        public ID3D11Texture2D? GetOrOpenTexture(int port)
        {
            if (port < 0 || port >= MatrixChannelConstants.TotalMatrixChannels) return null;

            lock (_syncLock)
            {
                RefreshChannel(port);
                return _openedTextures[port];
            }
        }

        public MatrixVideoChannelInfo GetChannelInfo(int port)
        {
            if (port < 0 || port >= MatrixChannelConstants.TotalMatrixChannels) return default;
            return _channelInfos[port];
        }

        public System.Collections.Generic.List<(int slot, string name, int width, int height, double fps, bool isActive)> GetDiscoveredSources()
        {
            var list = new System.Collections.Generic.List<(int slot, string name, int width, int height, double fps, bool isActive)>();
            for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
            {
                RefreshChannel(i);
                var info = _channelInfos[i];
                bool active = info.IsActive == 1 && info.Width > 0 && info.Height > 0 && (DateTime.UtcNow.Ticks - info.TimestampUtcTicks) < TimeSpan.FromSeconds(3).Ticks;
                string label = !string.IsNullOrWhiteSpace(info.SourceName) ? info.SourceName : (i == 0 ? "PGM Master (Slot 0)" : $"Slot {i}");
                list.Add((i, label, info.Width, info.Height, info.Fps, active));
            }
            return list;
        }

        public void RefreshAllChannels()
        {
            for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
            {
                RefreshChannel(i);
            }
        }

        public void RefreshChannel(int port)
        {
            if (port < 0 || port >= MatrixChannelConstants.TotalMatrixChannels) return;

            try
            {
                if (_channelAccessors[port] == null)
                {
                    if (!TryOpenChannelMmf(port)) return;
                }

                var accessor = _channelAccessors[port];
                if (accessor != null)
                {
                    accessor.Read(0, out MatrixVideoChannelInfo info);
                    _channelInfos[port] = info;

                    var handle = new IntPtr(info.SharedHandle);
                    if (handle != IntPtr.Zero && handle != _cachedHandles[port] && _device != null)
                    {
                        _openedTextures[port]?.Dispose();
                        _openedTextures[port] = null;

                        try
                        {
                            _openedTextures[port] = _device.OpenSharedResource<ID3D11Texture2D>(handle);
                            _cachedHandles[port] = handle;
                            Console.WriteLine($"[MatrixRouterSubscriber] Successfully mapped shared texture for Port {port} (Handle: 0x{handle:X})");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[MatrixRouterSubscriber] Failed to open shared texture Port {port}: {ex.Message}");
                        }
                    }

                    ChannelMetadataUpdated?.Invoke(port, info);
                }
            }
            catch
            {
                // Channel MMF closed or invalidated by publisher restart
                _channelAccessors[port]?.Dispose();
                _channelAccessors[port] = null;
                _channelMmfs[port]?.Dispose();
                _channelMmfs[port] = null;
            }
        }

        private void AudioReaderLoop()
        {
            byte[] readBuffer = new byte[32768];

            while (_audioReaderRunning && !_isDisposed)
            {
                try
                {
                    bool signaled = _audioSyncEvent?.WaitOne(20) ?? false;
                    if (!signaled && !_audioReaderRunning) break;

                    if (_audioAccessor == null) continue;

                    for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                    {
                        int slotBaseOffset = i * _audioSlotTotalSize;
                        int bufferBaseOffset = slotBaseOffset + _audioHeaderSize;

                        _audioAccessor.Read(slotBaseOffset, out MatrixAudioChannelHeader hdr);
                        if (hdr.IsActive == 0) continue;

                        long bytesAvailable = hdr.TotalBytesWritten - _lastReadTotalAudio[i];
                        if (bytesAvailable <= 0) continue;

                        int bytesToRead = (int)Math.Min(bytesAvailable, readBuffer.Length);
                        int readOffset = (int)(_lastReadTotalAudio[i] % MatrixRouterPublisher.AudioRingBufferSizePerChannel);

                        int firstChunk = Math.Min(bytesToRead, MatrixRouterPublisher.AudioRingBufferSizePerChannel - readOffset);
                        _audioAccessor.ReadArray(bufferBaseOffset + readOffset, readBuffer, 0, firstChunk);

                        if (firstChunk < bytesToRead)
                        {
                            int secondChunk = bytesToRead - firstChunk;
                            _audioAccessor.ReadArray(bufferBaseOffset, readBuffer, firstChunk, secondChunk);
                        }

                        _lastReadTotalAudio[i] += bytesToRead;

                        byte[] pcmCopy = new byte[bytesToRead];
                        Buffer.BlockCopy(readBuffer, 0, pcmCopy, 0, bytesToRead);
                        ChannelAudioReceived?.Invoke(i, pcmCopy, bytesToRead);
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                _audioReaderRunning = false;
                _audioSyncEvent?.Set();
                _audioReaderThread?.Join(200);

                for (int i = 0; i < MatrixChannelConstants.TotalMatrixChannels; i++)
                {
                    _openedTextures[i]?.Dispose();
                    _openedTextures[i] = null;
                    _channelAccessors[i]?.Dispose();
                    _channelAccessors[i] = null;
                    _channelMmfs[i]?.Dispose();
                    _channelMmfs[i] = null;
                }

                _metadataAccessor?.Dispose();
                _metadataMmf?.Dispose();

                _audioAccessor?.Dispose();
                _audioMmf?.Dispose();
                _audioSyncEvent?.Dispose();
            }
        }
    }
}
