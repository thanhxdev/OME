using System.Diagnostics;
using OpenMedia.SDK;
using OpenMedia.Platform.Internal;

namespace OpenMedia.Platform
{
    /// <summary>
    /// Provides device enumeration and capture for hardware inputs
    /// (webcams, DeckLink cards, desktop capture).
    /// </summary>
    /// <example>
    /// <code>
    /// var devices = await DeviceCapture.EnumerateDevicesAsync();
    /// var player = await DeviceCapture.OpenAsync("Logitech Webcam");
    /// player.AttachPreview(videoView);
    /// await player.PlayAsync();
    /// </code>
    /// </example>
    public static class DeviceCapture
    {
        private static IReadOnlyList<DeviceInfo>? _cachedDevices;
        private static readonly SemaphoreSlim _cacheLock = new(1, 1);

        /// <summary>
        /// Information about a capture device.
        /// </summary>
        public sealed class DeviceInfo
        {
            /// <summary>Human-readable device name.</summary>
            public string Name { get; init; } = string.Empty;

            /// <summary>Device type (Camera, DeckLink, Screen).</summary>
            public DeviceType Type { get; init; }

            /// <summary>Unique device identifier.</summary>
            public string DeviceId { get; init; } = string.Empty;

            /// <summary>Device capabilities description.</summary>
            public string Capabilities { get; init; } = string.Empty;
        }

        /// <summary>
        /// Type of capture device.
        /// </summary>
        public enum DeviceType
        {
            /// <summary>Webcam or USB camera (DirectShow).</summary>
            Camera,

            /// <summary>Blackmagic DeckLink capture card.</summary>
            DeckLink,

            /// <summary>Screen/desktop capture.</summary>
            Screen
        }

        /// <summary>
        /// Gets a cached list of available capture devices.
        /// The list is populated on first access via <see cref="EnumerateDevicesAsync"/>.
        /// Call <see cref="RefreshDevicesAsync"/> to force a re-scan.
        /// </summary>
        public static IReadOnlyList<DeviceInfo> Devices => _cachedDevices ?? Array.Empty<DeviceInfo>();

        /// <summary>
        /// Enumerates all available capture devices on the system.
        /// Queries DirectShow and DeckLink devices via the server.
        /// Results are cached for subsequent access via <see cref="Devices"/>.
        /// </summary>
        /// <returns>A list of available capture devices.</returns>
        public static async Task<IReadOnlyList<DeviceInfo>> EnumerateDevicesAsync()
        {
            if (_cachedDevices != null) return _cachedDevices;

            await _cacheLock.WaitAsync();
            try
            {
                if (_cachedDevices != null) return _cachedDevices;

                var devices = new List<DeviceInfo>();

                // Query DirectShow devices via IPC
                var directShowDevices = await QueryDirectShowDevicesAsync();
                devices.AddRange(directShowDevices);

                // Query DeckLink devices via IPC
                var deckLinkDevices = await QueryDeckLinkDevicesAsync();
                devices.AddRange(deckLinkDevices);

                // Add screen capture as a virtual device
                devices.Add(new DeviceInfo
                {
                    Name = "Desktop Capture",
                    Type = DeviceType.Screen,
                    DeviceId = "desktop://0",
                    Capabilities = "Screen capture, primary monitor"
                });

                _cachedDevices = devices;
                Trace.WriteLine($"[DeviceCapture] Enumerated {devices.Count} devices.");
                return _cachedDevices;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        /// <summary>
        /// Forces a re-scan of capture devices, clearing the cache.
        /// </summary>
        /// <returns>A fresh list of available capture devices.</returns>
        public static async Task<IReadOnlyList<DeviceInfo>> RefreshDevicesAsync()
        {
            _cachedDevices = null;
            return await EnumerateDevicesAsync();
        }

        /// <summary>
        /// Opens a capture device and returns a <see cref="MediaPlayer"/> connected to it.
        /// </summary>
        /// <param name="deviceName">Name of the device to open.</param>
        /// <returns>A <see cref="MediaPlayer"/> receiving frames from the device.</returns>
        public static async Task<MediaPlayer> OpenAsync(string deviceName)
        {
            var player = new MediaPlayer();
            await player.OpenAsync($"device://{deviceName}");
            return player;
        }

        /// <summary>
        /// Captures the desktop or a specific monitor.
        /// </summary>
        /// <param name="monitorIndex">Monitor index (0-based). <c>null</c> for primary monitor.</param>
        /// <returns>A <see cref="MediaPlayer"/> receiving desktop frames.</returns>
        public static async Task<MediaPlayer> CaptureDesktopAsync(int? monitorIndex = null)
        {
            var monitor = monitorIndex ?? 0;
            var player = new MediaPlayer();
            await player.OpenAsync($"desktop://{monitor}");
            return player;
        }

        // ─── IPC Device Queries ─────────────────────────────────────

        private static async Task<IEnumerable<DeviceInfo>> QueryDirectShowDevicesAsync()
        {
            var devices = new List<DeviceInfo>();

            try
            {
                if (!OpenMediaRuntime.IsConnected) return devices;

                var payload = IPCCommandBuilder.ListDevices("DirectShow");
                var response = await OpenMediaRuntime.SendCommandAsync(CommandType.ListDevices, payload);
                if (response != null)
                {
                    devices.AddRange(IPCCommandBuilder.ParseDeviceList(response, DeviceType.Camera));
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DeviceCapture] DirectShow query failed: {ex.Message}");
            }

            return devices;
        }

        private static async Task<IEnumerable<DeviceInfo>> QueryDeckLinkDevicesAsync()
        {
            var devices = new List<DeviceInfo>();

            try
            {
                if (!OpenMediaRuntime.IsConnected) return devices;

                var payload = IPCCommandBuilder.ListDevices("DeckLink");
                var response = await OpenMediaRuntime.SendCommandAsync(CommandType.ListDevices, payload);
                if (response != null)
                {
                    devices.AddRange(IPCCommandBuilder.ParseDeviceList(response, DeviceType.DeckLink));
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[DeviceCapture] DeckLink query failed: {ex.Message}");
            }

            return devices;
        }
    }
}
