using System;
using System.Threading.Tasks;

namespace OpenMedia.SDK
{
    public class SDKPipeline
    {
        private uint _id;

        public uint Id => _id;

        private SDKPipeline(uint id)
        {
            _id = id;
        }

        public static async Task<SDKPipeline> CreateAsync(string name, uint width = 1920, uint height = 1080, double fps = 60.0)
        {
            var builder = new MessageBuilder();
            builder.WriteString(name);
            builder.WriteU32(width);
            builder.WriteU32(height);
            builder.WriteF64(fps);

            byte[] response = await SDKEngine.Instance.IPC.SendAndReceiveAsync(CommandType.CreatePipeline, builder.ToArray());
            if (response != null && response.Length >= 4)
            {
                var reader = new MessageReader(response);
                uint id = reader.ReadU32();
                return new SDKPipeline(id);
            }

            throw new Exception("Failed to create pipeline via IPC");
        }

        public async Task<bool> StartAsync()
        {
            byte[] payload = BitConverter.GetBytes(_id);
            byte[] response = await SDKEngine.Instance.IPC.SendAndReceiveAsync(CommandType.StartPipeline, payload);
            return response != null;
        }

        public async Task<bool> StopAsync()
        {
            byte[] payload = BitConverter.GetBytes(_id);
            byte[] response = await SDKEngine.Instance.IPC.SendAndReceiveAsync(CommandType.StopPipeline, payload);
            return response != null;
        }

        public async Task<bool> SetLayerMuteAsync(uint layerIndex, bool muted)
        {
            return await SetLayerPropertiesAsync(layerIndex, muted, 1.0);
        }

        public async Task<bool> SetLayerPropertiesAsync(uint layerIndex, bool muted, double volume)
        {
            var builder = new MessageBuilder();
            builder.WriteU32(_id);
            builder.WriteU32(layerIndex);
            builder.WriteBool(muted);
            builder.WriteF64(volume);
            byte[] response = await SDKEngine.Instance.IPC.SendAndReceiveAsync(CommandType.SetLayerProperties, builder.ToArray());
            return response != null;
        }
    }
}
