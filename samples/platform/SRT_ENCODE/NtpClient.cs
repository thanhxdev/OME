using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SRT_ENCODE
{
    /// <summary>
    /// Kết quả truy vấn đồng bộ thời gian thực qua giao thức SNTP (RFC 5905).
    /// </summary>
    public sealed class NtpSyncResult
    {
        public bool Success { get; set; }
        public string Server { get; set; } = string.Empty;
        public double OffsetMs { get; set; }
        public double RoundTripDelayMs { get; set; }
        public DateTime ServerUtcTime { get; set; }
        public int Stratum { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public string GetFormattedOffset()
        {
            if (!Success) return "Sync Failed";
            string sign = OffsetMs >= 0 ? "+" : "";
            string stratumLabel = Stratum switch
            {
                1 => "Stratum 1 Atomic Primary",
                2 => "Stratum 2 NTP Secondary",
                _ => $"Stratum {Stratum} NTP"
            };
            return $"{sign}{OffsetMs:F2} ms ({stratumLabel} • RTT: {RoundTripDelayMs:F1} ms)";
        }
    }

    /// <summary>
    /// Thực hiện truy vấn thời gian thực tới các máy chủ NTP qua giao thức SNTP UDP Port 123.
    /// </summary>
    public static class NtpClient
    {
        public static async Task<NtpSyncResult> QueryTimeAsync(string ntpServer = "time.google.com", int timeoutMs = 3000)
        {
            var result = new NtpSyncResult
            {
                Server = ntpServer
            };

            if (string.IsNullOrWhiteSpace(ntpServer))
            {
                ntpServer = "time.google.com";
                result.Server = ntpServer;
            }

            try
            {
                // NTP Data packet structure (48 bytes)
                byte[] ntpData = new byte[48];
                // LeapIndicator = 0 (no warning), VersionNum = 4 (IPv4/IPv6), Mode = 3 (Client)
                ntpData[0] = 0x23; // 00 100 011

                using var cts = new CancellationTokenSource(timeoutMs);
                var addresses = await Dns.GetHostAddressesAsync(ntpServer, cts.Token).ConfigureAwait(false);
                if (addresses.Length == 0)
                {
                    result.ErrorMessage = $"Không phân giải được địa chỉ IP cho server: {ntpServer}";
                    return result;
                }

                var ipEndPoint = new IPEndPoint(addresses[0], 123);
                using var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp)
                {
                    ReceiveTimeout = timeoutMs,
                    SendTimeout = timeoutMs
                };

                DateTime t1 = DateTime.UtcNow; // Client transmit time request

                // Ghi T1 vào Transmit Timestamp field (bytes 40-47)
                WriteNtpTimestamp(ntpData, 40, t1);

                await socket.SendToAsync(ntpData, SocketFlags.None, ipEndPoint).ConfigureAwait(false);

                byte[] receiveBuffer = new byte[48];
                var receiveResult = await socket.ReceiveFromAsync(receiveBuffer, SocketFlags.None, ipEndPoint).ConfigureAwait(false);

                DateTime t4 = DateTime.UtcNow; // Client receive time

                if (receiveResult.ReceivedBytes < 48)
                {
                    result.ErrorMessage = "Gói tin phản hồi từ máy chủ NTP không đủ 48 bytes chuẩn.";
                    return result;
                }

                // Phân tích thông số từ gói tin NTP phản hồi
                int stratum = receiveBuffer[1];
                DateTime t2 = ReadNtpTimestamp(receiveBuffer, 32); // Server receive time
                DateTime t3 = ReadNtpTimestamp(receiveBuffer, 40); // Server transmit time

                // Công thức tính toán chuẩn NTP RFC 5905:
                // Offset = ((T2 - T1) + (T3 - T4)) / 2
                // RoundTripDelay = (T4 - T1) - (T3 - T2)
                double offsetMs = (((t2 - t1).TotalMilliseconds) + ((t3 - t4).TotalMilliseconds)) / 2.0;
                double delayMs = Math.Max(0.0, (t4 - t1).TotalMilliseconds - (t3 - t2).TotalMilliseconds);

                result.Success = true;
                result.Stratum = stratum;
                result.OffsetMs = offsetMs;
                result.RoundTripDelayMs = delayMs;
                result.ServerUtcTime = t3 + TimeSpan.FromMilliseconds(offsetMs);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static DateTime ReadNtpTimestamp(byte[] buffer, int offset)
        {
            ulong intPart = 0;
            for (int i = 0; i < 4; i++)
            {
                intPart = (intPart << 8) | buffer[offset + i];
            }

            ulong fractPart = 0;
            for (int i = 4; i < 8; i++)
            {
                fractPart = (fractPart << 8) | buffer[offset + i];
            }

            double milliseconds = (intPart * 1000.0) + ((fractPart * 1000.0) / 0x100000000L);
            // NTP Epoch: 1900-01-01 00:00:00 UTC
            return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds);
        }

        private static void WriteNtpTimestamp(byte[] buffer, int offset, DateTime dateTime)
        {
            var epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double totalMilliseconds = (dateTime - epoch).TotalMilliseconds;

            ulong intPart = (ulong)(totalMilliseconds / 1000.0);
            ulong fractPart = (ulong)(((totalMilliseconds % 1000.0) / 1000.0) * 0x100000000L);

            for (int i = 3; i >= 0; i--)
            {
                buffer[offset + i] = (byte)(intPart & 0xFF);
                intPart >>= 8;
            }

            for (int i = 7; i >= 4; i--)
            {
                buffer[offset + i] = (byte)(fractPart & 0xFF);
                fractPart >>= 8;
            }
        }
    }
}
