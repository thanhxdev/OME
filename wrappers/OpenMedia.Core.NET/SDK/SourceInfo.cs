using System;

namespace OpenMedia.SDK
{
    public struct SourceInfo
    {
        public string Url;
        public double DurationMs;
        public uint Width;
        public uint Height;
        public double FrameRate;
        public string VideoCodec;
        public string AudioCodec;
        public int AudioChannels;
        public int AudioSampleRate;
        public long BitrateKbps;
    }
}
