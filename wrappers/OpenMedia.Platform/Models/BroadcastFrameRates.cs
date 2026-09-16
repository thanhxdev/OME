using System;

namespace OpenMedia.Platform.Models
{
    /// <summary>
    /// Định nghĩa chuẩn các tỷ lệ khung hình phát sóng truyền hình (Broadcast Rational Frame Rates).
    /// Tuyệt đối sử dụng tỉ số nguyên (Numerator / Denominator) thay vì floating-point để triệt tiêu hiện tượng trôi pha (drift) 0.1%.
    /// </summary>
    public static class BroadcastFrameRates
    {
        // ─── NTSC Family (Fractional 1000/1001) ──────────────────────────────────
        public static readonly (int num, int den) Fps23_976 = (24000, 1001); // 23.9760239... fps
        public static readonly (int num, int den) Fps29_97  = (30000, 1001); // 29.9700299... fps
        public static readonly (int num, int den) Fps59_94  = (60000, 1001); // 59.9400599... fps
        public static readonly (int num, int den) Fps119_88 = (120000, 1001); // 119.880119... fps

        // ─── PAL / SECAM Family (Integer European standard) ─────────────────────
        public static readonly (int num, int den) Fps25     = (25, 1);
        public static readonly (int num, int den) Fps50     = (50, 1);
        public static readonly (int num, int den) Fps100    = (100, 1);

        // ─── Film / Integer Family ──────────────────────────────────────────────
        public static readonly (int num, int den) Fps24     = (24, 1);
        public static readonly (int num, int den) Fps30     = (30, 1);
        public static readonly (int num, int den) Fps60     = (60, 1);
        public static readonly (int num, int den) Fps120    = (120, 1);

        /// <summary>
        /// Ánh xạ từ số thực (double) sang cặp tỉ số nguyên chuẩn phát sóng gần nhất.
        /// Giúp bảo toàn độ chính xác 1001 mẫu mà không làm lệch pha âm thanh / hình ảnh theo thời gian.
        /// </summary>
        public static (int num, int den) SnapToRational(double fps)
        {
            if (fps <= 0.5) return Fps59_94;

            // 1. Kiểm tra các tần số quét nguyên chính xác trước (24, 25, 30, 50, 60, 100, 120)
            double rounded = Math.Round(fps);
            if (Math.Abs(fps - rounded) < 0.01)
            {
                int intFps = (int)rounded;
                return intFps switch
                {
                    24 => Fps24,
                    25 => Fps25,
                    30 => Fps30,
                    50 => Fps50,
                    60 => Fps60,
                    100 => Fps100,
                    120 => Fps120,
                    _ => (intFps, 1)
                };
            }

            // 2. Kiểm tra các tần số quét NTSC chuẩn (mẫu số 1001)
            if (Math.Abs(fps - 23.976) < 0.01 || Math.Abs(fps - (24000.0 / 1001.0)) < 0.005) return Fps23_976;
            if (Math.Abs(fps - 29.97) < 0.01 || Math.Abs(fps - (30000.0 / 1001.0)) < 0.005) return Fps29_97;
            if (Math.Abs(fps - 59.94) < 0.015 || Math.Abs(fps - (60000.0 / 1001.0)) < 0.005) return Fps59_94;
            if (Math.Abs(fps - 119.88) < 0.02 || Math.Abs(fps - (120000.0 / 1001.0)) < 0.005) return Fps119_88;

            // Xấp xỉ phân số với mẫu số 1001 nếu gần dạng NTSC
            double mult1001 = fps * 1001.0;
            double roundMult = Math.Round(mult1001);
            if (Math.Abs(mult1001 - roundMult) < 0.5)
            {
                return ((int)roundMult, 1001);
            }

            // Fallback: phân số với mẫu số 100
            int num100 = (int)Math.Round(fps * 100.0);
            return (num100, 100);
        }

        /// <summary>
        /// Tạo tham số định dạng cho FFmpeg (ví dụ: "60000/1001" hoặc "25").
        /// </summary>
        public static string FormatFfmpeg((int num, int den) rational)
        {
            return rational.den == 1 ? $"{rational.num}" : $"{rational.num}/{rational.den}";
        }

        /// <summary>
        /// Tạo tham số định dạng cho FFmpeg từ số thực.
        /// </summary>
        public static string FormatFfmpeg(double fps)
        {
            return FormatFfmpeg(SnapToRational(fps));
        }

        /// <summary>
        /// Tính thời lượng 1 frame theo timebase 90kHz (tiêu chuẩn MPEG-TS PES / PCR).
        /// </summary>
        public static long CalculateFrameDuration90k(int num, int den)
        {
            if (num <= 0) return 3000;
            return (long)(90000L * den) / num;
        }
    }
}
