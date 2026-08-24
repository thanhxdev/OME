using System;
using System.Threading.Tasks;

namespace OpenMedia.SDK
{
    public class SDKSource
    {
        private uint _pipelineId;
        private uint _sourceId;

        public SDKSource(uint pipelineId, uint sourceId)
        {
            _pipelineId = pipelineId;
            _sourceId = sourceId;
        }

        public static async Task<SDKSource> CreateAsync(SDKPipeline pipeline, uint sourceId, string url, bool loop = true)
        {
            var builder = new MessageBuilder();
            builder.WriteU32(pipeline.Id);
            builder.WriteU32(sourceId);
            builder.WriteString(url);
            builder.WriteBool(loop); // loop mode
            builder.WriteU32(0); // startMs

            byte[] response = await SDKEngine.Instance.IPC.SendAndReceiveAsync(CommandType.OpenSource, builder.ToArray());
            if (response != null)
            {
                return new SDKSource(pipeline.Id, sourceId);
            }

            throw new Exception("Failed to open source via IPC");
        }

        public async Task<SourceInfo> GetInfoAsync()
        {
            var builder = new MessageBuilder();
            builder.WriteU32(_pipelineId);
            builder.WriteU32(_sourceId);

            byte[] response = await SDKEngine.Instance.IPC.SendAndReceiveAsync(CommandType.GetSourceInfo, builder.ToArray());
            if (response != null && response.Length > 0)
            {
                var reader = new MessageReader(response);
                var info = new SourceInfo();
                info.Url = reader.ReadString();
                info.DurationMs = reader.ReadF64();
                info.Width = reader.ReadU32();
                info.Height = reader.ReadU32();
                info.FrameRate = reader.ReadF64();
                info.VideoCodec = reader.ReadString();
                info.AudioCodec = reader.ReadString();
                info.AudioChannels = reader.ReadI32();
                info.AudioSampleRate = reader.ReadI32();
                info.BitrateKbps = (long)reader.ReadU64();
                return info;
            }

            throw new Exception("Failed to get source info via IPC");
        }
    }
}
