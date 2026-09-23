using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Controls;

namespace SRT_ENCODE
{
    /// <summary>
    /// Thông tin chi tiết của một Card mạng cục bộ (NIC).
    /// </summary>
    public sealed class NicInfo
    {
        public string IpAddress { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Helper quét và quản lý danh sách Card mạng (NIC) IPv4 phục vụ cho việc gán cổng phát SRT & SMPTE 2022-7.
    /// </summary>
    public static class NetworkInterfaceScanner
    {
        public const string DefaultAnyIp = "0.0.0.0";
        public const string DefaultAnyLabel = "0.0.0.0 (Mặc định - Tự động chọn NIC)";

        /// <summary>
        /// Quét toàn bộ card mạng đang hoạt động và trả về danh sách IPv4 unicast khả dụng.
        /// </summary>
        public static List<NicInfo> GetAvailableNetworkInterfaces()
        {
            var list = new List<NicInfo>
            {
                new NicInfo
                {
                    IpAddress = DefaultAnyIp,
                    DisplayName = DefaultAnyLabel
                }
            };

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = unicast.Address.ToString();
                            string name = ni.Name;
                            string desc = string.IsNullOrWhiteSpace(ni.Description) ? name : ni.Description;
                            string label = $"{name} - {desc} ({ip})";

                            if (!list.Any(x => x.IpAddress == ip))
                            {
                                list.Add(new NicInfo
                                {
                                    IpAddress = ip,
                                    DisplayName = label
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback graceful
            }

            return list;
        }

        /// <summary>
        /// Nạp danh sách NIC vào một ComboBox và chọn lại chính xác địa chỉ IP trước đó.
        /// </summary>
        public static void PopulateNicComboBox(ComboBox cmb, string? selectedIp, List<NicInfo>? nics = null)
        {
            if (cmb == null) return;
            nics ??= GetAvailableNetworkInterfaces();

            string currentIp = !string.IsNullOrWhiteSpace(selectedIp)
                ? selectedIp
                : (cmb.SelectedValue as string) ?? (cmb.SelectedItem as NicInfo)?.IpAddress ?? DefaultAnyIp;

            cmb.ItemsSource = null;
            cmb.DisplayMemberPath = "DisplayName";
            cmb.SelectedValuePath = "IpAddress";
            cmb.ItemsSource = nics;

            var matched = nics.FirstOrDefault(n => string.Equals(n.IpAddress, currentIp, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                cmb.SelectedItem = matched;
            }
            else if (nics.Count > 0)
            {
                cmb.SelectedIndex = 0;
            }
        }
    }
}
