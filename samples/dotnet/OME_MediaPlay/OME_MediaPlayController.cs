using System;
using System.Threading.Tasks;
using OpenMedia.SDK;

namespace OME_MediaPlay
{
    public class OME_MediaPlayController
    {
        private IPCClient _ipcClient;
        private uint _activePipelineId = 0;

        public bool IsConnected => _ipcClient != null && _ipcClient.IsConnected;
        public bool IsVideoLoaded => _activePipelineId > 0;

        /// <summary>
        /// Khởi tạo kết nối IPC với OpenMediaServer.exe
        /// </summary>
        public async Task<bool> ConnectServerAsync()
        {
            // SDKEngine tự động tìm và chạy OpenMediaServer.exe nếu chưa bật
            string serverPath = @"C:\Users\ASUS NUC\Desktop\Code\OME\build\bin\Debug\OpenMediaServer.exe";
            bool success = await SDKEngine.Instance.InitializeAsync(pipeName: "OpenMediaSDK", serverPath: serverPath);
            if (success)
            {
                _ipcClient = SDKEngine.Instance.IPC;
            }
            return success;
        }

        /// <summary>
        /// Gửi lệnh nạp File Video và Dựng Pipeline trên Server
        /// </summary>
        public async Task<bool> LoadVideoAsync(string videoPath)
        {
            var msg = new MessageBuilder();
            msg.WriteString("MainPipeline"); // Pipeline name
            msg.WriteU32(1920); // Target Width
            msg.WriteU32(1080); // Target Height
            msg.WriteF64(60.0); // Target FPS

            byte[] response = await _ipcClient.SendAndReceiveAsync(CommandType.CreatePipeline, msg.ToArray());
            if (response != null && response.Length >= 4)
            {
                _activePipelineId = BitConverter.ToUInt32(response, 0);
                
                // Now open the source
                var srcMsg = new MessageBuilder();
                srcMsg.WriteU32(_activePipelineId);
                srcMsg.WriteU32(0); // Source ID 0
                srcMsg.WriteString(videoPath);
                srcMsg.WriteBool(true); // Loop
                srcMsg.WriteI32(0); // StartMs
                
                byte[] srcResponse = await _ipcClient.SendAndReceiveAsync(CommandType.OpenSource, srcMsg.ToArray());
                return srcResponse != null;
            }
            return false;
        }

        public async Task PlayAsync() => await SendPipelineControlCmdAsync((ushort)CommandType.StartPipeline);  // CMD_PLAY
        public async Task PauseAsync() => await SendPipelineControlCmdAsync((ushort)CommandType.PausePipeline); // CMD_PAUSE

        /// <summary>
        /// Gửi lệnh Tua Video (Seekbar)
        /// </summary>
        public async Task SeekToSecondsAsync(double seconds)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            msg.WriteU64((ulong)(seconds * 1000)); // position_ms

            await _ipcClient.SendAndReceiveAsync((CommandType)0x15, msg.ToArray()); // CMD_SEEK
        }

        /// <summary>
        /// Gửi lệnh Đổi Độ phân giải thời gian thực (1080p, 720p, 480p...)
        /// </summary>
        public async Task ChangeResolutionAsync(int width, int height)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            msg.WriteU32((uint)width);
            msg.WriteU32((uint)height);

            await _ipcClient.SendAndReceiveAsync((CommandType)0x20, msg.ToArray()); // CMD_SET_RESOLUTION
        }

        /// <summary>
        /// Gửi lệnh Đổi FPS thời gian thực (60, 30, 24 FPS)
        /// </summary>
        public async Task ChangeFPSAsync(float fps)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            msg.WriteF64(fps);

            await _ipcClient.SendAndReceiveAsync((CommandType)0x21, msg.ToArray()); // CMD_SET_FPS
        }

        /// <summary>
        /// Gửi lệnh Điều chỉnh Volume / Mute
        /// </summary>
        public async Task SetVolumeAsync(float volumeGain)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            msg.WriteF64(volumeGain); // 0.0f (Mute) -> 1.0f (100%)

            await _ipcClient.SendAndReceiveAsync((CommandType)0x30, msg.ToArray()); // CMD_SET_VOLUME
        }

        private async Task SendPipelineControlCmdAsync(ushort cmdType)
        {
            var msg = new MessageBuilder();
            msg.WriteU32(_activePipelineId);
            await _ipcClient.SendAndReceiveAsync((CommandType)cmdType, msg.ToArray());
        }

        public async Task DisconnectAsync()
        {
            if (IsConnected)
            {
                await _ipcClient.ShutdownServerAsync();
            }
            await SDKEngine.Instance.ShutdownAsync();
        }

        public async Task<SourceInfo?> GetSourceInfoAsync()
        {
            if (!IsConnected || !IsVideoLoaded) return null;
            
            var builder = new MessageBuilder();
            builder.WriteU32(_activePipelineId);
            builder.WriteU32(0); // Source 0

            byte[] response = await _ipcClient.SendAndReceiveAsync(CommandType.GetSourceInfo, builder.ToArray());
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
            return null;
        }

        public async Task<ShareD3D11TexturePayload?> RequestSharedTextureAsync()
        {
            if (!IsConnected || !IsVideoLoaded) return null;
            return await _ipcClient.RequestSharedTextureAsync();
        }
    }
}
