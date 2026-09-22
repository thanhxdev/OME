using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace OME_PLAYOUT
{
    /// <summary>
    /// High-performance Video File Reader & Pre-roll Decoder Engine for Broadcast Playout.
    /// Reads MP4, ProRes, MXF, MOV, MKV files using FFmpeg rawvideo BGRA pipe with an asynchronous pre-roll queue.
    /// Non-blocking: never freezes or blocks the WPF UI thread during load, seek, or transitions.
    /// Also provides synthetic broadcast test patterns if no physical media file is available.
    /// </summary>
    public sealed class ClipPlayoutReader : IDisposable
    {
        private const int TargetWidth = 1920;
        private const int TargetHeight = 1080;
        public const int FrameBytes = TargetWidth * TargetHeight * 4;
        private const int MaxPreRollFrames = 60; // ~1 second buffer at 59.94 fps

        private readonly ConcurrentQueue<byte[]> _frameQueue = new();
        private readonly ConcurrentQueue<byte[]> _poolQueue = new();
        private readonly object _stateLock = new();

        private Process? _ffmpegProcess;
        private Process? _ffmpegAudioProcess;
        private Thread? _readerThread;
        private Thread? _audioReaderThread;
        private readonly ConcurrentQueue<byte[]> _audioQueue = new();
        public const int AudioSampleRate = 48000;
        public const int AudioChannels = 2;
        public const int AudioBits = 16;
        public const int AudioChunkSize = 3200; // ~1 frame at 60 fps (48000*4/60)

        private volatile bool _isRunning;
        private volatile bool _isPaused;
        private volatile bool _isDisposed;

        public string CurrentFilePath { get; private set; } = string.Empty;
        public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(2);
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public bool IsLooping { get; set; } = true;
        public bool IsPreRolled => _frameQueue.Count >= 5 || _syntheticActive;
        public int BufferedFrameCount => _frameQueue.Count;
        public bool IsFileLoaded => !string.IsNullOrEmpty(CurrentFilePath) && File.Exists(CurrentFilePath);

        private volatile bool _syntheticActive = false;
        private long _syntheticFrameCount = 0;
        private byte[]? _lastFrame;

        public ClipPlayoutReader()
        {
            // Pre-allocate buffer pool to eliminate GC pressure at 60 FPS
            for (int i = 0; i < MaxPreRollFrames + 5; i++)
            {
                _poolQueue.Enqueue(new byte[FrameBytes]);
            }
        }

        public void DiscardAudioChunk()
        {
            _audioQueue.TryDequeue(out _);
        }

        public bool Open(string filePath, TimeSpan? customDuration = null, bool forceRestart = false)
        {
            lock (_stateLock)
            {
                if (!forceRestart && IsFileLoaded && string.Equals(CurrentFilePath, filePath, StringComparison.OrdinalIgnoreCase) && _isRunning)
                {
                    return true;
                }

                CloseInternal();

                CurrentFilePath = filePath ?? string.Empty;
                Duration = customDuration ?? TimeSpan.FromMinutes(3);
                Position = TimeSpan.Zero;

                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    _syntheticActive = true;
                    _isRunning = true;
                    return true;
                }

                string? ffmpegPath = FindFfmpegExecutable();
                if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
                {
                    _syntheticActive = true;
                    _isRunning = true;
                    return true;
                }

                _syntheticActive = false;
                _isRunning = true;

                // Launch video decoder in background thread - NEVER block the UI thread!
                _readerThread = new Thread(() => ReadFramesWorker(ffmpegPath, filePath))
                {
                    Name = "ClipPlayout_VideoDecoder",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _readerThread.Start();

                // Launch audio decoder in background thread
                _audioReaderThread = new Thread(() => ReadAudioWorker(ffmpegPath, filePath))
                {
                    Name = "ClipPlayout_AudioDecoder",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _audioReaderThread.Start();

                return true;
            }
        }

        private void ReadFramesWorker(string ffmpegPath, string filePath)
        {
            while (_isRunning && !_isDisposed)
            {
                Process? proc = null;
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-threads 2 -i \"{filePath}\" -f rawvideo -pix_fmt bgra -s {TargetWidth}x{TargetHeight} -r 59.94 -",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    proc = new Process { StartInfo = startInfo };
                    proc.ErrorDataReceived += (s, e) => { /* Suppress stderr log */ };

                    if (!proc.Start())
                    {
                        _syntheticActive = true;
                        break;
                    }

                    proc.BeginErrorReadLine();

                    lock (_stateLock)
                    {
                        if (!_isRunning || _isDisposed)
                        {
                            SafeKillProcess(proc);
                            break;
                        }
                        _ffmpegProcess = proc;
                    }

                    var stdout = proc.StandardOutput.BaseStream;
                    bool eofReached = false;

                    while (_isRunning && !_isDisposed && !proc.HasExited)
                    {
                        if (_isPaused)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        // If pre-roll queue is full, throttle reading to match consumption
                        if (_frameQueue.Count >= MaxPreRollFrames)
                        {
                            Thread.Sleep(5);
                            continue;
                        }

                        if (!_poolQueue.TryDequeue(out byte[]? frameBuffer))
                        {
                            frameBuffer = new byte[FrameBytes];
                        }

                        int totalRead = 0;
                        while (totalRead < FrameBytes && _isRunning && !_isDisposed)
                        {
                            int bytesToRead = FrameBytes - totalRead;
                            int read = stdout.Read(frameBuffer, totalRead, bytesToRead);
                            if (read <= 0) break; // EOF
                            totalRead += read;
                        }

                        if (totalRead == FrameBytes)
                        {
                            _frameQueue.Enqueue(frameBuffer);
                        }
                        else
                        {
                            _poolQueue.Enqueue(frameBuffer);
                            eofReached = (totalRead == 0 && _isRunning);
                            break; // EOF reached
                        }
                    }

                    SafeKillProcess(proc);
                    lock (_stateLock)
                    {
                        if (_ffmpegProcess == proc) _ffmpegProcess = null;
                    }

                    // Only loop if EOF was naturally reached, NOT if aborted/killed
                    if (eofReached && IsLooping && _isRunning && !_isDisposed)
                    {
                        Position = TimeSpan.Zero;
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
                catch
                {
                    _syntheticActive = true;
                    if (proc != null) SafeKillProcess(proc);
                    break;
                }
            }
        }

        private void ReadAudioWorker(string ffmpegPath, string filePath)
        {
            while (_isRunning && !_isDisposed)
            {
                Process? proc = null;
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-i \"{filePath}\" -vn -f s16le -acodec pcm_s16le -ac 2 -ar 48000 -",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    proc = new Process { StartInfo = startInfo };
                    proc.ErrorDataReceived += (s, e) => { };

                    if (!proc.Start()) break;
                    proc.BeginErrorReadLine();

                    lock (_stateLock)
                    {
                        if (!_isRunning || _isDisposed)
                        {
                            SafeKillProcess(proc);
                            break;
                        }
                        _ffmpegAudioProcess = proc;
                    }

                    var stdout = proc.StandardOutput.BaseStream;
                    bool eofReached = false;

                    while (_isRunning && !_isDisposed && !proc.HasExited)
                    {
                        if (_isPaused)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        if (_audioQueue.Count >= 120)
                        {
                            Thread.Sleep(5);
                            continue;
                        }

                        byte[] buffer = new byte[AudioChunkSize];
                        int totalRead = 0;
                        while (totalRead < AudioChunkSize && _isRunning && !_isDisposed)
                        {
                            int read = stdout.Read(buffer, totalRead, AudioChunkSize - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        if (totalRead > 0)
                        {
                            _audioQueue.Enqueue(buffer);
                        }
                        else
                        {
                            eofReached = (totalRead == 0 && _isRunning);
                            break; // EOF
                        }
                    }

                    SafeKillProcess(proc);
                    lock (_stateLock)
                    {
                        if (_ffmpegAudioProcess == proc) _ffmpegAudioProcess = null;
                    }

                    if (eofReached && IsLooping && _isRunning && !_isDisposed)
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
                catch
                {
                    if (proc != null) SafeKillProcess(proc);
                    break;
                }
            }
        }

        public bool TryGetNextAudioChunk(byte[] destinationBuffer, out int bytesRead)
        {
            bytesRead = 0;
            if (destinationBuffer == null || destinationBuffer.Length == 0) return false;

            if (_audioQueue.TryDequeue(out byte[]? chunk))
            {
                int copyLen = Math.Min(chunk.Length, destinationBuffer.Length);
                Buffer.BlockCopy(chunk, 0, destinationBuffer, 0, copyLen);
                bytesRead = copyLen;
                return true;
            }

            return false;
        }

        public bool TryGetNextFrame(byte[] destinationBuffer)
        {
            if (destinationBuffer == null || destinationBuffer.Length < FrameBytes) return false;

            if (_syntheticActive)
            {
                GenerateSyntheticFrame(destinationBuffer);
                return true;
            }

            if (_frameQueue.TryDequeue(out byte[]? frame))
            {
                Buffer.BlockCopy(frame, 0, destinationBuffer, 0, FrameBytes);
                _lastFrame = frame;
                _poolQueue.Enqueue(frame);
                Position += TimeSpan.FromSeconds(1.0 / 59.94);
                return true;
            }

            if (_lastFrame != null)
            {
                // Repeat last frame under buffer underrun to protect on-air feed
                Buffer.BlockCopy(_lastFrame, 0, destinationBuffer, 0, FrameBytes);
                return true;
            }

            GenerateSyntheticFrame(destinationBuffer);
            return true;
        }

        private void GenerateSyntheticFrame(byte[] destination)
        {
            _syntheticFrameCount++;
            int phase = (int)(_syntheticFrameCount % 1920);

            unsafe
            {
                fixed (byte* pDst = destination)
                {
                    int* pDst32 = (int*)pDst;
                    int lineOffset = phase * 2;

                    for (int y = 0; y < TargetHeight; y++)
                    {
                        int rowIdx = y * TargetWidth;
                        int shade = (y * 255) / TargetHeight;

                        for (int x = 0; x < TargetWidth; x++)
                        {
                            // Broadcast test pattern: Deep Blue gradient with moving cyber cyan scanning bar
                            int barDist = Math.Abs((x + lineOffset) % TargetWidth - (TargetWidth / 2));
                            if (barDist < 8)
                            {
                                pDst32[rowIdx + x] = unchecked((int)0xFF00E5FF); // Bright Cyan
                            }
                            else if (barDist < 24)
                            {
                                pDst32[rowIdx + x] = unchecked((int)0xFF0284C7); // Blue
                            }
                            else
                            {
                                int r = 10;
                                int g = 18;
                                int b = 32 + (shade / 6);
                                pDst32[rowIdx + x] = (255 << 24) | (r << 16) | (g << 8) | b;
                            }
                        }
                    }
                }
            }

            Position += TimeSpan.FromSeconds(1.0 / 59.94);
        }

        public void Pause() => _isPaused = true;
        public void Resume() => _isPaused = false;

        public void Close()
        {
            lock (_stateLock)
            {
                CloseInternal();
            }
        }

        private void CloseInternal()
        {
            _isRunning = false;
            _isPaused = false;

            var oldReader = _readerThread;
            var oldAudio = _audioReaderThread;

            if (_ffmpegProcess != null)
            {
                SafeKillProcess(_ffmpegProcess);
                _ffmpegProcess = null;
            }

            if (_ffmpegAudioProcess != null)
            {
                SafeKillProcess(_ffmpegAudioProcess);
                _ffmpegAudioProcess = null;
            }

            // Guarantee old threads have terminated to prevent zombie race conditions
            if (oldReader != null && oldReader.IsAlive && Thread.CurrentThread != oldReader)
            {
                try { oldReader.Join(150); } catch { }
            }
            if (oldAudio != null && oldAudio.IsAlive && Thread.CurrentThread != oldAudio)
            {
                try { oldAudio.Join(150); } catch { }
            }

            _readerThread = null;
            _audioReaderThread = null;
            _lastFrame = null;
            _syntheticActive = false;

            while (_frameQueue.TryDequeue(out byte[]? f))
            {
                _poolQueue.Enqueue(f);
            }

            while (_audioQueue.TryDequeue(out _)) { }
        }

        private static void SafeKillProcess(Process proc)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                }
            }
            catch { }
            finally
            {
                try { proc.Dispose(); } catch { }
            }
        }

        private static string? FindFfmpegExecutable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidatePaths = new[]
            {
                Path.Combine(baseDir, @"..\..\..\..\..\third_party\ffmpeg\bin\ffmpeg.exe"),
                Path.Combine(baseDir, @"..\..\..\..\third_party\ffmpeg\bin\ffmpeg.exe"),
                Path.Combine(baseDir, @"..\..\..\third_party\ffmpeg\bin\ffmpeg.exe"),
                Path.Combine(baseDir, @"third_party\ffmpeg\bin\ffmpeg.exe"),
                @"C:\Users\ASUS NUC\Desktop\Code\OME\third_party\ffmpeg\bin\ffmpeg.exe",
                "ffmpeg.exe"
            };

            foreach (var path in candidatePaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch { }
            }

            return null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Close();
        }
    }
}
