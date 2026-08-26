using OpenMedia.SDK;

namespace OpenMedia.Platform.Internal
{
    /// <summary>
    /// Provides typed helper methods for building IPC commands
    /// on top of <see cref="MessageBuilder"/> and <see cref="MessageReader"/>.
    /// </summary>
    internal static class IPCCommandBuilder
    {
        /// <summary>
        /// Builds a CreatePipeline command payload.
        /// </summary>
        internal static byte[] CreatePipeline(string name, uint width = 1920, uint height = 1080, double fps = 60.0)
        {
            var b = new MessageBuilder();
            b.WriteString(name);
            b.WriteU32(width);
            b.WriteU32(height);
            b.WriteF64(fps);
            return b.ToArray();
        }

        /// <summary>
        /// Builds an OpenSource command payload.
        /// </summary>
        internal static byte[] OpenSource(uint pipelineId, uint sourceId, string url, bool loop = false, uint startMs = 0)
        {
            var b = new MessageBuilder();
            b.WriteU32(pipelineId);
            b.WriteU32(sourceId);
            b.WriteString(url);
            b.WriteBool(loop);
            b.WriteU32(startMs);
            return b.ToArray();
        }

        /// <summary>
        /// Builds a pipeline control command payload (Start/Stop/Pause).
        /// </summary>
        internal static byte[] PipelineControl(uint pipelineId)
        {
            return BitConverter.GetBytes(pipelineId);
        }

        /// <summary>
        /// Builds a GetSourceInfo command payload.
        /// </summary>
        internal static byte[] GetSourceInfo(uint pipelineId, uint sourceId)
        {
            var b = new MessageBuilder();
            b.WriteU32(pipelineId);
            b.WriteU32(sourceId);
            return b.ToArray();
        }

        /// <summary>
        /// Parses a CreatePipeline response to extract the pipeline ID.
        /// </summary>
        internal static uint? ParsePipelineId(byte[]? response)
        {
            if (response == null || response.Length < 4) return null;
            var reader = new MessageReader(response);
            return reader.ReadU32();
        }

        /// <summary>
        /// Parses a GetSourceInfo response into a <see cref="MediaInfo"/>.
        /// </summary>
        internal static MediaInfo? ParseSourceInfo(byte[]? response, string sourceUri)
        {
            if (response == null || response.Length == 0) return null;

            var r = new MessageReader(response);
            return new MediaInfo
            {
                SourceUri = r.ReadString(),
                Duration = TimeSpan.FromMilliseconds(r.ReadF64()),
                Width = (int)r.ReadU32(),
                Height = (int)r.ReadU32(),
                FrameRate = r.ReadF64(),
                VideoCodec = r.ReadString(),
                AudioCodec = r.ReadString(),
                AudioChannels = r.ReadI32(),
                AudioSampleRate = r.ReadI32(),
                BitrateKbps = (long)r.ReadU64()
            };
        }

        /// <summary>
        /// Builds a handshake payload.
        /// </summary>
        internal static byte[] Handshake(string clientVersion = "1.0.0-Platform")
        {
            var b = new MessageBuilder();
            b.WriteString(clientVersion);
            return b.ToArray();
        }

        /// <summary>
        /// Parses a handshake response. Returns <c>true</c> if server responded "OK".
        /// </summary>
        internal static bool ParseHandshake(byte[]? response)
        {
            if (response == null || response.Length == 0) return false;
            var reader = new MessageReader(response);
            return reader.ReadString() == "OK";
        }

        /// <summary>
        /// Builds a ListDevices command payload.
        /// </summary>
        /// <param name="deviceType">Device category to query: "DirectShow", "DeckLink", or "All".</param>
        internal static byte[] ListDevices(string deviceType = "All")
        {
            var b = new MessageBuilder();
            b.WriteString(deviceType);
            return b.ToArray();
        }

        /// <summary>
        /// Parses a ListDevices response into device info objects.
        /// </summary>
        internal static IEnumerable<DeviceCapture.DeviceInfo> ParseDeviceList(
            byte[] response, DeviceCapture.DeviceType type)
        {
            var devices = new List<DeviceCapture.DeviceInfo>();
            if (response.Length == 0) return devices;

            try
            {
                var r = new MessageReader(response);
                var count = r.ReadU32();

                for (uint i = 0; i < count; i++)
                {
                    devices.Add(new DeviceCapture.DeviceInfo
                    {
                        Name = r.ReadString(),
                        DeviceId = r.ReadString(),
                        Type = type,
                        Capabilities = r.ReadString()
                    });
                }
            }
            catch
            {
                // Partial parse — return what we got
            }

            return devices;
        }

        /// <summary>
        /// Builds an AddOutput command payload for attaching a stream output to a pipeline.
        /// </summary>
        internal static byte[] BuildOutputConfig(uint pipelineId, StreamOutput output)
        {
            return output.ToIPCPayload(pipelineId);
        }
    }
}
