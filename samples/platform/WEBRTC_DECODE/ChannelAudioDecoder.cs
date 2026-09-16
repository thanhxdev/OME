#pragma warning disable CS0618 // Concentus factory methods

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Concentus.Structs;

namespace WEBRTC_DECODE
{
    /// <summary>
    /// Decodes incoming Opus audio packets into raw 48kHz, 16-bit, stereo PCM.
    /// Supports high-performance managed Concentus Opus decoding (RFC 6716 / RFC 7587),
    /// direct PCM pass-through for uncompressed audio, and FFmpeg pipe fallback.
    /// </summary>
    public sealed class ChannelAudioDecoder : IDisposable
    {
        private readonly int _channelIndex;
        private readonly OpusDecoder _opusDecoder;
        private readonly short[] _opusPcmScratch = new short[960 * 2]; // 20ms @ 48kHz stereo = 1920 samples
        private byte[] _opusPcmOutBytes = new byte[3840];
        private readonly object _decoderLock = new();

        private Process? _ffmpegProc;
        private Stream? _stdin;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private bool _isDisposed;

        public event Action<int, byte[], int>? PcmAudioReceived; // channelIndex, pcmBytes, length
        public bool IsRunning => (_opusDecoder != null) || (_ffmpegProc != null && !_ffmpegProc.HasExited);

        public ChannelAudioDecoder(int channelIndex)
        {
            _channelIndex = channelIndex;
            _opusDecoder = new OpusDecoder(48000, 2);
        }

        public void Start()
        {
            if (_ffmpegProc != null && !_ffmpegProc.HasExited) return;

            try
            {
                // FFmpeg receives audio stream and outputs uncompressed s16le 48000Hz stereo PCM
                // Note: -flags low_delay -probesize 32 -analyzeduration 0 for real-time audio
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-hide_banner -loglevel error -flags low_delay " +
                                "-probesize 32 -analyzeduration 0 -f ogg -i pipe:0 " +
                                "-vn -sn -dn -acodec pcm_s16le -ar 48000 -ac 2 -f s16le pipe:1",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _ffmpegProc = Process.Start(startInfo);
                if (_ffmpegProc == null) return;

                _stdin = _ffmpegProc.StandardInput.BaseStream;
                _cts = new CancellationTokenSource();

                _readTask = Task.Run(() => ReadPcmLoop(_ffmpegProc.StandardOutput.BaseStream, _cts.Token));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChannelAudioDecoder {_channelIndex}] Failed to start FFmpeg fallback: {ex.Message}");
            }
        }

        public void FeedOpusPacket(byte[] opusPayload)
        {
            if (_isDisposed || opusPayload == null || opusPayload.Length == 0) return;

            // 1. Direct PCM Pass-through: If payload is raw 48kHz 16-bit stereo PCM (e.g. 3840 bytes for 20ms or 1920 bytes for 10ms)
            if (opusPayload.Length == 3840 || opusPayload.Length == 1920)
            {
                PcmAudioReceived?.Invoke(_channelIndex, opusPayload, opusPayload.Length);
                return;
            }

            // 2. FFmpeg Ogg container stream
            bool isOgg = opusPayload.Length >= 4 && opusPayload[0] == 0x4F && opusPayload[1] == 0x67 && opusPayload[2] == 0x67 && opusPayload[3] == 0x53; // "OggS"
            if (isOgg && _stdin != null)
            {
                try
                {
                    lock (_stdin)
                    {
                        _stdin.Write(opusPayload, 0, opusPayload.Length);
                        _stdin.Flush();
                    }
                }
                catch { }
                return;
            }

            // 3. RFC 7587 Standard WebRTC Opus Packet Decoding via Concentus
            try
            {
                lock (_decoderLock)
                {
                    int decodedPerChannel = _opusDecoder.Decode(opusPayload, 0, opusPayload.Length, _opusPcmScratch, 0, 960, false);
                    if (decodedPerChannel > 0)
                    {
                        int totalShorts = decodedPerChannel * 2;
                        int totalBytes = totalShorts * 2;
                        if (_opusPcmOutBytes.Length < totalBytes)
                        {
                            _opusPcmOutBytes = new byte[totalBytes];
                        }

                        for (int i = 0; i < totalShorts; i++)
                        {
                            short sample = _opusPcmScratch[i];
                            _opusPcmOutBytes[i * 2] = (byte)(sample & 0xFF);
                            _opusPcmOutBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                        }

                        PcmAudioReceived?.Invoke(_channelIndex, _opusPcmOutBytes, totalBytes);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChannelAudioDecoder {_channelIndex}] Concentus decode error: {ex.Message}");
            }

            // Fallback: pass-through if decode fails
            PcmAudioReceived?.Invoke(_channelIndex, opusPayload, opusPayload.Length);
        }

        private void ReadPcmLoop(Stream stdout, CancellationToken token)
        {
            // 48000Hz * 2 channels * 2 bytes/sample * 20ms = 3840 bytes per 20ms audio frame
            byte[] buffer = new byte[3840];

            try
            {
                while (!token.IsCancellationRequested && !_isDisposed)
                {
                    int totalRead = 0;
                    while (totalRead < buffer.Length && !token.IsCancellationRequested)
                    {
                        int read = stdout.Read(buffer, totalRead, buffer.Length - totalRead);
                        if (read <= 0) break;
                        totalRead += read;
                    }

                    if (totalRead > 0)
                    {
                        PcmAudioReceived?.Invoke(_channelIndex, buffer, totalRead);
                    }
                    else
                    {
                        Thread.Sleep(5);
                    }
                }
            }
            catch
            {
                // Pipe closed
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _stdin?.Close();
                if (_ffmpegProc != null && !_ffmpegProc.HasExited)
                {
                    _ffmpegProc.Kill();
                }
                _ffmpegProc?.Dispose();
                _ffmpegProc = null;
            }
            catch { }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
            _cts?.Dispose();
        }
    }
}
