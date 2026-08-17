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
    }
}
