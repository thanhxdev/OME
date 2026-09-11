using System;
using System.Net.Sockets;

namespace WEBRTC_DECODE
{
    public sealed class ReceiverChannelState
    {
        public int ChannelIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsRunning { get; set; }
        public bool IsConnected { get; set; }
        public double CurrentRttMs { get; set; }
        public double CurrentPacketLoss { get; set; }
        public double CurrentBitrateKbps { get; set; }
        public double CurrentFps { get; set; }
        public double BufferHealthPercent { get; set; } = 100.0;
        public double CurrentJitterMs { get; set; }
        public ulong SequenceGaps { get; set; }
        public ulong TotalBytesReceived { get; set; }
        public TimeSpan Uptime { get; set; } = TimeSpan.Zero;
        public string StatusMessage { get; set; } = "Standby / Idle";
        public string SfuUrl { get; set; } = "http://127.0.0.1:4000";
        public int VideoPort { get; set; }
        public int AudioPort { get; set; }
        public bool IsSinglePortMode { get; set; } = false;
    }
}