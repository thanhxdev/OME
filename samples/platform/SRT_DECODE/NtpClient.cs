using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SRT_DECODE
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
                byte[] ntpData = new byte[48];
                ntpData[0] = 0x23; // Leap = 0, Version = 4, Mode = 3 (Client)

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

                int stratum = receiveBuffer[1];
                DateTime t2 = ReadNtpTimestamp(receiveBuffer, 32); // Server receive time
                DateTime t3 = ReadNtpTimestamp(receiveBuffer, 40); // Server transmit time

                // Offset = ((T2 - T1) + (T3 - T4)) / 2
                double offsetMs = ((t2 - t1).TotalMilliseconds + (t3 - t4).TotalMilliseconds) / 2.0;
                // Delay = (T4 - T1) - (T3 - T2)
                double delayMs = (t4 - t1).TotalMilliseconds - (t3 - t2).TotalMilliseconds;

                result.Success = true;
                result.OffsetMs = offsetMs;
                result.RoundTripDelayMs = Math.Max(0.1, delayMs);
                result.ServerUtcTime = t4.AddMilliseconds(offsetMs);
                result.Stratum = stratum;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static void WriteNtpTimestamp(byte[] buffer, int offset, DateTime dateTime)
        {
            DateTime epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan span = dateTime - epoch;
            ulong totalSeconds = (ulong)span.TotalSeconds;
            ulong fractions = (ulong)((span.TotalSeconds - totalSeconds) * 4294967296.0);

            for (int i = 3; i >= 0; i--)
            {
                buffer[offset + i] = (byte)(totalSeconds & 0xFF);
                totalSeconds >>= 8;
            }
            for (int i = 7; i >= 4; i--)
            {
                buffer[offset + i] = (byte)(fractions & 0xFF);
                fractions >>= 8;
            }
        }

        private static DateTime ReadNtpTimestamp(byte[] buffer, int offset)
        {
            ulong intPart = 0;
            for (int i = 0; i < 4; i++)
            {
                intPart = (intPart << 8) | buffer[offset + i];
            }

            ulong fracPart = 0;
            for (int i = 4; i < 8; i++)
            {
                fracPart = (fracPart << 8) | buffer[offset + i];
            }

            double milliseconds = (intPart * 1000.0) + ((fracPart * 1000.0) / 4294967296.0);
            return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(milliseconds);
        }
    }
}
