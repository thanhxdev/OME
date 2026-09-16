using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenMedia.Platform.Models
{
    /// <summary>
    /// Cấu trúc dữ liệu mô tả đầy đủ thông số luồng truyền dẫn đa phương tiện theo kiến trúc Medialooks.
    /// Có khả năng tuần tự hóa và giải tuần tự hóa gọn nhẹ qua SRT StreamID hoặc JSON metadata.
    /// </summary>
    public sealed class MediaStreamInfo
    {
        // ─── Video Parameters ──────────────────────────────────────────
        [JsonPropertyName("v")]
        public string VideoCodec { get; set; } = "h264";

        [JsonPropertyName("w")]
        public int Width { get; set; } = 1920;

        [JsonPropertyName("h")]
        public int Height { get; set; } = 1080;

        [JsonPropertyName("fps_num")]
        public int FrameRateNum { get; set; } = 60000;

        [JsonPropertyName("fps_den")]
        public int FrameRateDen { get; set; } = 1001;

        [JsonPropertyName("tb_num")]
        public int TimebaseNum { get; set; } = 1;

        [JsonPropertyName("tb_den")]
        public int TimebaseDen { get; set; } = 90000;

        [JsonPropertyName("pix")]
        public string PixelFormat { get; set; } = "yuv420p";

        [JsonPropertyName("prof")]
        public int Profile { get; set; } = 0;

        [JsonPropertyName("lvl")]
        public int Level { get; set; } = 0;

        [JsonPropertyName("il")]
        public bool Interlaced { get; set; } = false;

        // ─── Audio Parameters ──────────────────────────────────────────
        [JsonPropertyName("a")]
        public string AudioCodec { get; set; } = "aac";

        [JsonPropertyName("sr")]
        public int AudioSampleRate { get; set; } = 48000;

        [JsonPropertyName("ch")]
        public int AudioChannels { get; set; } = 2;

        [JsonPropertyName("abr")]
        public int AudioBitrateKbps { get; set; } = 192;

        // ─── Encoder Metadata ──────────────────────────────────────────
        [JsonPropertyName("br")]
        public int BitrateKbps { get; set; } = 6000;

        [JsonPropertyName("enc")]
        public string EncoderName { get; set; } = "OpenMedia";

        // ─── Computed Properties ───────────────────────────────────────
        [JsonIgnore]
        public double FrameRateDouble => FrameRateDen > 0 ? (double)FrameRateNum / FrameRateDen : 0.0;

        [JsonIgnore]
        public long FrameDuration90k => FrameRateNum > 0 ? (long)(90000L * FrameRateDen) / FrameRateNum : 3000L;

        /// <summary>
        /// Tạo bản sao đầy đủ thông số.
        /// </summary>
        public MediaStreamInfo Clone() => (MediaStreamInfo)MemberwiseClone();

        /// <summary>
        /// Đóng gói metadata thành chuỗi gọn nhẹ nhúng vào SRT StreamID (ví dụ: path?fmt=omi&v=h264&w=1920...).
        /// Độ dài chuẩn chỉ ~80-100 ký tự, hoàn toàn an toàn trong giới hạn 512 bytes của SRT.
        /// </summary>
        public string SerializeToStreamId(string basePath = "")
        {
            var sb = new StringBuilder();
            string cleanPath = basePath?.Trim() ?? string.Empty;
            sb.Append(cleanPath);

            char sep = cleanPath.Contains('?') ? '&' : '?';
            sb.Append(sep).Append("fmt=omi");
            sb.Append("&v=").Append(Uri.EscapeDataString(VideoCodec ?? "h264"));
            sb.Append("&w=").Append(Width);
            sb.Append("&h=").Append(Height);
            sb.Append("&fps=").Append(FrameRateNum).Append('/').Append(FrameRateDen);
            sb.Append("&a=").Append(Uri.EscapeDataString(AudioCodec ?? "aac"));
            sb.Append("&sr=").Append(AudioSampleRate);
            sb.Append("&ch=").Append(AudioChannels);
            sb.Append("&br=").Append(BitrateKbps);

            return sb.ToString();
        }

        /// <summary>
        /// Trích xuất MediaStreamInfo từ chuỗi SRT StreamID nhận được khi kết nối.
        /// </summary>
        public static bool TryParseFromStreamId(string? streamId, out MediaStreamInfo info, out string basePath)
        {
            info = new MediaStreamInfo();
            basePath = streamId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(streamId))
            {
                return false;
            }

            int queryIdx = streamId.IndexOf('?');
            if (queryIdx < 0)
            {
                basePath = streamId;
                return false;
            }

            basePath = streamId.Substring(0, queryIdx);
            string queryString = streamId.Substring(queryIdx + 1);

            var pairs = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in pairs)
            {
                int eqIdx = pair.IndexOf('=');
                if (eqIdx > 0)
                {
                    string key = pair.Substring(0, eqIdx);
                    string val = Uri.UnescapeDataString(pair.Substring(eqIdx + 1));
                    dict[key] = val;
                }
            }

            // Kiểm tra xem có chứa thông số nhận dạng broadcast OME không
            bool hasFormat = dict.ContainsKey("fmt") || (dict.ContainsKey("w") && dict.ContainsKey("h"));
            if (!hasFormat)
            {
                return false;
            }

            if (dict.TryGetValue("v", out var v) && !string.IsNullOrEmpty(v)) info.VideoCodec = v;
            if (dict.TryGetValue("w", out var wStr) && int.TryParse(wStr, out int w) && w > 0) info.Width = w;
            if (dict.TryGetValue("h", out var hStr) && int.TryParse(hStr, out int h) && h > 0) info.Height = h;

            if (dict.TryGetValue("fps", out var fpsStr) && !string.IsNullOrEmpty(fpsStr))
            {
                int slashIdx = fpsStr.IndexOf('/');
                if (slashIdx > 0 &&
                    int.TryParse(fpsStr.Substring(0, slashIdx), out int num) &&
                    int.TryParse(fpsStr.Substring(slashIdx + 1), out int den) && den > 0)
                {
                    info.FrameRateNum = num;
                    info.FrameRateDen = den;
                }
                else if (double.TryParse(fpsStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedFps))
                {
                    var rational = BroadcastFrameRates.SnapToRational(parsedFps);
                    info.FrameRateNum = rational.num;
                    info.FrameRateDen = rational.den;
                }
            }

            if (dict.TryGetValue("a", out var a) && !string.IsNullOrEmpty(a)) info.AudioCodec = a;
            if (dict.TryGetValue("sr", out var srStr) && int.TryParse(srStr, out int sr) && sr > 0) info.AudioSampleRate = sr;
            if (dict.TryGetValue("ch", out var chStr) && int.TryParse(chStr, out int ch) && ch > 0) info.AudioChannels = ch;
            if (dict.TryGetValue("br", out var brStr) && int.TryParse(brStr, out int br) && br > 0) info.BitrateKbps = br;

            return true;
        }

        public string SerializeJson() => JsonSerializer.Serialize(this);

        public static MediaStreamInfo? DeserializeJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<MediaStreamInfo>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
