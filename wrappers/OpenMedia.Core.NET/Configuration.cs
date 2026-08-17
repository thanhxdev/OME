using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenMedia.SDK
{
    public class EngineConfig
    {
        [JsonPropertyName("threads")]
        public int Threads { get; set; } = 4;

        [JsonPropertyName("log_level")]
        public string LogLevel { get; set; } = "info";

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static EngineConfig FromJson(string json)
        {
            return JsonSerializer.Deserialize<EngineConfig>(json) ?? new EngineConfig();
        }
    }

    public class VideoFormatConfig
    {
        [JsonPropertyName("width")]
        public int Width { get; set; } = 1920;

        [JsonPropertyName("height")]
        public int Height { get; set; } = 1080;

        [JsonPropertyName("fps_num")]
        public int FpsNum { get; set; } = 60;

        [JsonPropertyName("fps_den")]
        public int FpsDen { get; set; } = 1;

        [JsonPropertyName("pixel_format")]
        public PixelFormat PixelFormat { get; set; } = PixelFormat.NV12;
    }
}
