using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
                // Robust FFmpeg audio decoder parameters:
                // - probesize 1000000 & analyzeduration 1000000 (1MB / 1s) reliably detects audio stream across all video bitrates (File, SDI, NDI)
                // - -map 0:a? maps the first available audio track without failing if audio PES is delayed
                // - nobuffer and low_delay flags eliminate internal latency
                // - s16le 48000Hz 2ch directly matches Windows sound card / mixer output
                string args = "-hide_banner -loglevel warning -probesize 1000000 -analyzeduration 1000000 -fflags nobuffer+flush_packets -flags low_delay -f mpegts -i pipe:0 -map 0:a? -vn -sn -dn -f s16le -ar 48000 -ac 2 pipe:1";

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
                _cts = new CancellationTokenSource();
                _isRunning = true;

                var token = _cts.Token;
                var proc = _process;
                var stdout = _stdout;

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

                // Drain stdout audio chunks (48000 Hz * 2ch * 2 bytes * 0.02s = 3840 bytes per 20ms chunk)
                _ = Task.Run(async () =>
                {
                    byte[] buffer = new byte[3840];
                    try
                    {
                        while (!token.IsCancellationRequested && _isRunning && !proc.HasExited)
                        {
                            int read = await stdout.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                            if (read > 0)
                            {
                                byte[] chunk = new byte[read];
                                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                                PcmAudioDecoded?.Invoke(_channelIndex, chunk, read);
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
            if (!_isRunning || _stdin == null || _process == null || _process.HasExited || data == null || length <= 0) return;

            try
            {
                _stdin.Write(data, 0, length);
                _stdin.Flush();
            }
            catch { }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                _stdin?.Close();
                _stdin = null;

                _stdout?.Close();
                _stdout = null;

                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        _process.Kill();
                        _process.WaitForExit(300);
                    }
                    catch { }
                    _process.Dispose();
                    _process = null;
                }
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
