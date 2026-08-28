using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OpenMedia.SDK
{
    public enum CommandType : uint
    {
        Handshake = 0x0001,
        GetStatus = 0x0003,
        Shutdown = 0x0006,
        ListDevices = 0x0007,
        CreatePipeline = 0x0100,
        DestroyPipeline = 0x0101,
        StartPipeline = 0x0102,
        StopPipeline = 0x0103,
        PausePipeline = 0x0104,
        ResumePipeline = 0x0105,
        GetPipelineState = 0x0106,
        GetPipelineInfo = 0x0107,
        OpenSource = 0x0200,
        SeekSource = 0x0202,
        GetSourceInfo = 0x0203,
        AddMixerInput = 0x0300,
        SetLayerProperties = 0x0303,
        ShareD3D11Texture = 0x0704,
        SetAVDelay = 0x0108
    }

    // C++ struct MessageHeader
    [StructLayout(LayoutKind.Sequential)]
    public struct MessageHeader
    {
        public uint Magic;
        public ushort Version;
        public CommandType CommandType;
        public uint SequenceNumber;
        public uint PayloadSize;
        public ulong Timestamp;
        public uint ClientId;
    }

    // C++ struct ResponseHeader
    [StructLayout(LayoutKind.Sequential)]
    public struct ResponseHeader
    {
        public uint Magic;
        public ushort Version;
        public uint Status;
        public uint SequenceNumber;
        public uint PayloadSize;
        public uint ErrorCode;
        public ulong Timestamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ShareD3D11TexturePayload
    {
        public ulong NtHandle0;
        public ulong NtHandle1;
        public uint Width;
        public uint Height;
        public uint BufferCount;
    }

    public class IPCClient : IDisposable
    {
        private NamedPipeClientStream _pipeClient;
        private readonly System.Threading.SemaphoreSlim _pipeLock = new System.Threading.SemaphoreSlim(1, 1);
        private uint _sequence = 0;

        public bool IsConnected => _pipeClient != null && _pipeClient.IsConnected;

        public async Task<bool> ConnectAsync(string pipeName = "OpenMediaIPC", int timeout = 5000)
        {
            try
            {
                _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipeClient.ConnectAsync(timeout);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<ShareD3D11TexturePayload?> RequestSharedTextureAsync()
        {
            if (_pipeClient == null || !_pipeClient.IsConnected) return null;

            try
            {
                await _pipeLock.WaitAsync();
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            try
            {
                if (_pipeClient == null || !_pipeClient.IsConnected) return null;

                // 1. Send Request
                var header = new MessageHeader
                {
                    Magic = 0x4F4D4549, // "OMEI"
                    Version = 1,
                    CommandType = CommandType.ShareD3D11Texture,
                    SequenceNumber = ++_sequence,
                    PayloadSize = 0,
                    Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ClientId = 1
                };

                byte[] headerBytes = StructureToByteArray(header);
                await _pipeClient.WriteAsync(headerBytes, 0, headerBytes.Length);

                // 2. Read Response Header
                byte[] respHeaderBytes = new byte[Marshal.SizeOf<ResponseHeader>()];
                int read = await _pipeClient.ReadAsync(respHeaderBytes, 0, respHeaderBytes.Length);
                if (read != respHeaderBytes.Length) return null;

                var respHeader = ByteArrayToStructure<ResponseHeader>(respHeaderBytes);
                if (respHeader.Status != 0) return null; // Status != Success

                // 3. Read Payload
                if (respHeader.PayloadSize == Marshal.SizeOf<ShareD3D11TexturePayload>())
                {
                    byte[] payloadBytes = new byte[respHeader.PayloadSize];
                    read = await _pipeClient.ReadAsync(payloadBytes, 0, payloadBytes.Length);
                    if (read == payloadBytes.Length)
                    {
                        return ByteArrayToStructure<ShareD3D11TexturePayload>(payloadBytes);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { _pipeLock.Release(); } catch { }
            }
        }

        public async Task<byte[]> SendAndReceiveAsync(CommandType type, byte[]? payload = null)
        {
            if (_pipeClient == null || !_pipeClient.IsConnected) return null;

            try
            {
                await _pipeLock.WaitAsync();
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            try
            {
                if (_pipeClient == null || !_pipeClient.IsConnected) return null;

                uint payloadSize = (uint)(payload?.Length ?? 0);
                var header = new MessageHeader
                {
                    Magic = 0x4F4D4549,
                    Version = 1,
                    CommandType = type,
                    SequenceNumber = ++_sequence,
                    PayloadSize = payloadSize,
                    Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ClientId = 1
                };

                // 1. Send Header + Payload combined in a single write operation (critical for message mode pipes)
                byte[] headerBytes = StructureToByteArray(header);
                if (payloadSize > 0 && payload != null)
                {
                    byte[] combined = new byte[headerBytes.Length + payload.Length];
                    Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
                    Buffer.BlockCopy(payload, 0, combined, headerBytes.Length, payload.Length);
                    await _pipeClient.WriteAsync(combined, 0, combined.Length);
                }
                else
                {
                    await _pipeClient.WriteAsync(headerBytes, 0, headerBytes.Length);
                }

                // 2. Read Response Header
                byte[] respHeaderBytes = new byte[Marshal.SizeOf<ResponseHeader>()];
                int read = await _pipeClient.ReadAsync(respHeaderBytes, 0, respHeaderBytes.Length);
                if (read != respHeaderBytes.Length) return null;

                var respHeader = ByteArrayToStructure<ResponseHeader>(respHeaderBytes);
                if (respHeader.Status != 0) return null; // Status != Success

                // 3. Read Response Payload
                if (respHeader.PayloadSize > 0)
                {
                    byte[] respPayload = new byte[respHeader.PayloadSize];
                    read = await _pipeClient.ReadAsync(respPayload, 0, respPayload.Length);
                    if (read == respPayload.Length)
                    {
                        return respPayload;
                    }
                }

                return new byte[0]; // Empty payload on success
            }
            catch
            {
                return null;
            }
            finally
            {
                try { _pipeLock.Release(); } catch { }
            }
        }

        public async Task<bool> ShutdownServerAsync()
        {
            if (_pipeClient == null || !_pipeClient.IsConnected) return false;
            try
            {
                byte[] resp = await SendAndReceiveAsync(CommandType.Shutdown);
                return resp != null;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] StructureToByteArray<T>(T obj) where T : struct
        {
            int size = Marshal.SizeOf(obj);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(obj, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
                return arr;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public void Dispose()
        {
            _pipeLock?.Dispose();
            _pipeClient?.Dispose();
        }
    }
}
