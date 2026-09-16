using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SRT_DECODE
{
    /// <summary>
    /// Decodes audio tracks from MPEG-TS stream into raw 16-bit 48kHz Stereo PCM using low-latency FFmpeg.
    /// Supports AAC, MP2, MP3, AC3, E-AC3, Opus, PCM, and SMPTE SDI embedded audio.
    /// </summary>
    public sealed class ChannelAudioDecoder : IDisposable
    {
        private readonly int _channelIndex;
        private Process? _process;
        private Stream? _stdin;
        private Stream? _stdout;
        private Channel<byte[]>? _inputChannel;
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private bool _disposed;

        public int ChannelIndex => _channelIndex;
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Fired when decoded 48kHz 16-bit Stereo PCM audio chunk is available.
        /// (channelIndex, pcmBytes, byteCount)
        /// </summary>
        public event Action<int, byte[], int>? PcmAudioDecoded;
        public event Action<string, string>? LogEmitted;

        public ChannelAudioDecoder(int channelIndex)
        {
            _channelIndex = channelIndex;
        }

        public bool Start()
        {
            if (_isRunning) return true;

            try
            {
                // Low-latency FFmpeg audio decoder parameters:
                // - probesize 1000k & analyzeduration 1000k ensures full detection of AAC/MP2/SMPTE stream headers
                // - -map 0:a? maps the first available audio track
                // - nobuffer and low_delay flags eliminate internal latency
                // - s16le 48000Hz 2ch directly matches Windows sound card / mixer output
                string args = "-hide_banner -loglevel warning -err_detect ignore_err -probesize 1000k -analyzeduration 1000k -thread_queue_size 512 -fflags nobuffer -flags low_delay -f mpegts -i pipe:0 -map 0:a? -vn -sn -dn -f s16le -ar 48000 -ac 2 pipe:1";

                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _process = Process.Start(psi);
                if (_process == null)
                {
                    Log("[ERROR]", $"Không thể khởi chạy Audio Decoder cho Cam {_channelIndex + 1}");
                    return false;
                }

                _stdin = _process.StandardInput.BaseStream;
                _stdout = _process.StandardOutput.BaseStream;
                // Unbounded channel: NEVER drop compressed audio TS packets
                _inputChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
                _cts = new CancellationTokenSource();
                _isRunning = true;

                var token = _cts.Token;
                var proc = _process;
                var stdout = _stdout;
                var stdin = _stdin;
                var inputChannel = _inputChannel;

                // Dedicated decoupled background task to pump TS data into Audio FFmpeg stdin with batch coalescing
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var reader = inputChannel.Reader;
                        byte[] batchBuffer = new byte[32768];
                        int batchLen = 0;

                        while (!token.IsCancellationRequested && await reader.WaitToReadAsync(token).ConfigureAwait(false))
                        {
                            while (reader.TryRead(out var chunk))
                            {
                                if (proc.HasExited || stdin == null) return;

                                if (chunk.Length > batchBuffer.Length - batchLen)
                                {
                                    if (batchLen > 0)
                                    {
                                        await stdin.WriteAsync(batchBuffer.AsMemory(0, batchLen), token).ConfigureAwait(false);
                                        batchLen = 0;
                                    }
                                }

                                if (chunk.Length >= batchBuffer.Length)
                                {
                                    await stdin.WriteAsync(chunk.AsMemory(0, chunk.Length), token).ConfigureAwait(false);
                                }
                                else
                                {
                                    Buffer.BlockCopy(chunk, 0, batchBuffer, batchLen, chunk.Length);
                                    batchLen += chunk.Length;
                                }
                            }

                            if (batchLen > 0)
                            {
                                if (proc.HasExited || stdin == null) return;
                                await stdin.WriteAsync(batchBuffer.AsMemory(0, batchLen), token).ConfigureAwait(false);
                                batchLen = 0;
                            }

                            if (stdin != null)
                            {
                                await stdin.FlushAsync(token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                }, token);

                // Drain stderr loop for diagnostics
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var reader = proc.StandardError;
                        while (!token.IsCancellationRequested && !proc.HasExited)
                        {
                            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) break;
                            if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                            {
                                Log("[FFMPEG-ERR]", line);
                            }
                        }
                    }
                    catch { }
                }, token);

                // Drain stdout audio chunks (48000 Hz * 2ch * 2 bytes = 4 bytes per frame)
                // Ensures 100% 4-byte stereo frame alignment to prevent channel swap and sample corruption
                _ = Task.Run(async () =>
                {
                    byte[] readBuffer = new byte[4096];
                    byte[] remainder = new byte[4];
                    int remainderCount = 0;

                    try
                    {
                        while (!token.IsCancellationRequested && _isRunning && !proc.HasExited)
                        {
                            int bytesRead = await stdout.ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), token).ConfigureAwait(false);
                            if (bytesRead > 0)
                            {
                                int totalBytes = remainderCount + bytesRead;
                                int alignedBytes = totalBytes - (totalBytes % 4); // Always 4-byte frame aligned

                                if (alignedBytes > 0)
                                {
                                    byte[] chunk = new byte[alignedBytes];
                                    int destOffset = 0;

                                    if (remainderCount > 0)
                                    {
                                        Buffer.BlockCopy(remainder, 0, chunk, 0, remainderCount);
                                        destOffset = remainderCount;
                                    }

                                    int fromRead = alignedBytes - remainderCount;
                                    if (fromRead > 0)
                                    {
                                        Buffer.BlockCopy(readBuffer, 0, chunk, destOffset, fromRead);
                                    }

                                    // Save any new remainder (< 4 bytes)
                                    int newRemainderCount = totalBytes - alignedBytes;
                                    if (newRemainderCount > 0)
                                    {
                                        Buffer.BlockCopy(readBuffer, fromRead, remainder, 0, newRemainderCount);
                                    }
                                    remainderCount = newRemainderCount;

                                    PcmAudioDecoded?.Invoke(_channelIndex, chunk, alignedBytes);
                                }
                                else
                                {
                                    // Less than 4 bytes accumulated in total, just store in remainder
                                    Buffer.BlockCopy(readBuffer, 0, remainder, remainderCount, bytesRead);
                                    remainderCount += bytesRead;
                                }
                            }
                            else
                            {
                                await Task.Delay(2, token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                }, token);

                Log("[AUDIO]", $"✅ Khởi động Audio Decoder thành công cho Cam {_channelIndex + 1} (48kHz Stereo PCM)");
                return true;
            }
            catch (Exception ex)
            {
                Log("[ERROR]", $"Lỗi khởi động Audio Decoder Cam {_channelIndex + 1}: {ex.Message}");
                _isRunning = false;
                return false;
            }
        }

        public void FeedData(byte[] data, int length)
        {
            if (!_isRunning || _inputChannel == null || data == null || length <= 0) return;

            try
            {
                byte[] chunk = new byte[length];
                Buffer.BlockCopy(data, 0, chunk, 0, length);
                _inputChannel.Writer.TryWrite(chunk);
            }
            catch { }
        }

        public void Flush()
        {
            // Handled asynchronously in decoupled pump
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            try
            {
                _cts?.Cancel();

                try { _inputChannel?.Writer.TryComplete(); } catch { }
                _inputChannel = null;

                // Kill process tree first so readers unblock immediately
                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        _process.Kill(entireProcessTree: true);
                        _process.WaitForExit(150);
                    }
                    catch { }
                    try { _process.Dispose(); } catch { }
                    _process = null;
                }

                try { _stdin?.Close(); } catch { }
                _stdin = null;

                try { _stdout?.Close(); } catch { }
                _stdout = null;

                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }
            catch { }
        }

        private void Log(string tag, string message)
        {
            LogEmitted?.Invoke(tag, message);
            Trace.WriteLine($"[ChannelAudioDecoder]{tag} {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
