#pragma warning disable CS0618 // Concentus factory methods

using System;
using Concentus.Enums;
using Concentus.Structs;

namespace WEBRTC_DECODE
{
    /// <summary>
    /// High-efficiency Opus Voice (VOIP) audio codec for real-time Intercom communication.
    /// Uses Concentus (100% pure managed C# Opus implementation, RFC 6716).
    /// Compresses 48kHz 16-bit PCM (3840 bytes/20ms) down to ~80-100 bytes (32 kbps),
    /// achieving >97% bandwidth reduction for field 4G/5G transmissions.
    /// </summary>
    public sealed class IntercomOpusCodec : IDisposable
    {
        private const int SampleRate = 48000;
        private const int FrameSize = 960; // 20ms @ 48kHz
        private const int DefaultBitrate = 32000; // 32 kbps VOIP

        private readonly OpusEncoder _encoder;
        private readonly OpusDecoder _decoder;
        private readonly short[] _pcmScratchMono = new short[FrameSize];
        private readonly byte[] _encodeBuffer = new byte[1024];
        private readonly object _lock = new();
        private bool _disposed;

        public IntercomOpusCodec(int bitrate = DefaultBitrate)
        {
            // Mono VOIP encoder for talkback mic
            _encoder = new OpusEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = bitrate;
            _encoder.Complexity = 5;
            _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

            // Mono decoder for incoming voice
            _decoder = new OpusDecoder(SampleRate, 1);
        }

        /// <summary>
        /// Encodes 20ms 16-bit PCM audio into a compact Opus packet (~80-100 bytes).
        /// Accepts either Mono (1920 bytes) or Stereo (3840 bytes) PCM bytes.
        /// </summary>
        public byte[] EncodePcm(byte[] pcmBytes, int offset, int count)
        {
            if (_disposed || pcmBytes == null || count <= 0) return Array.Empty<byte>();

            lock (_lock)
            {
                // Convert bytes to mono short samples
                int samplesAvailable;
                if (count >= FrameSize * 4) // Stereo (3840 bytes) -> downmix to mono
                {
                    samplesAvailable = FrameSize;
                    for (int i = 0; i < FrameSize; i++)
                    {
                        int byteIdx = offset + (i * 4);
                        short l = (short)(pcmBytes[byteIdx] | (pcmBytes[byteIdx + 1] << 8));
                        short r = (short)(pcmBytes[byteIdx + 2] | (pcmBytes[byteIdx + 3] << 8));
                        _pcmScratchMono[i] = (short)((l + r) / 2);
                    }
                }
                else // Mono (1920 bytes)
                {
                    samplesAvailable = Math.Min(FrameSize, count / 2);
                    for (int i = 0; i < samplesAvailable; i++)
                    {
                        int byteIdx = offset + (i * 2);
                        _pcmScratchMono[i] = (short)(pcmBytes[byteIdx] | (pcmBytes[byteIdx + 1] << 8));
                    }
                    // Zero-pad if needed
                    for (int i = samplesAvailable; i < FrameSize; i++)
                    {
                        _pcmScratchMono[i] = 0;
                    }
                }

                int encodedLength = _encoder.Encode(_pcmScratchMono, 0, FrameSize, _encodeBuffer, 0, _encodeBuffer.Length);
                if (encodedLength <= 0) return Array.Empty<byte>();

                byte[] result = new byte[encodedLength];
                Buffer.BlockCopy(_encodeBuffer, 0, result, 0, encodedLength);
                return result;
            }
        }

        /// <summary>
        /// Decodes an Opus packet back into 20ms 16-bit Stereo 48kHz PCM bytes (3840 bytes),
        /// ready for playback through standard audio hardware.
        /// </summary>
        public byte[] DecodeToStereoPcm(byte[] opusData, int offset, int length)
        {
            if (_disposed || opusData == null || length <= 0) return Array.Empty<byte>();

            lock (_lock)
            {
                int decodedSamples = _decoder.Decode(opusData, offset, length, _pcmScratchMono, 0, FrameSize, false);
                if (decodedSamples <= 0) return Array.Empty<byte>();

                // Convert mono decoded samples to stereo 16-bit PCM bytes (3840 bytes)
                byte[] stereoBytes = new byte[decodedSamples * 4];
                for (int i = 0; i < decodedSamples; i++)
                {
                    short sample = _pcmScratchMono[i];
                    byte b0 = (byte)(sample & 0xFF);
                    byte b1 = (byte)((sample >> 8) & 0xFF);

                    int dIdx = i * 4;
                    stereoBytes[dIdx + 0] = b0; // L
                    stereoBytes[dIdx + 1] = b1;
                    stereoBytes[dIdx + 2] = b0; // R
                    stereoBytes[dIdx + 3] = b1;
                }

                return stereoBytes;
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
