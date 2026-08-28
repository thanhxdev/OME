using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OpenMedia.SDK
{
    public class MessageBuilder
    {
        private readonly MemoryStream _ms = new MemoryStream();
        private readonly BinaryWriter _writer;

        public MessageBuilder()
        {
            _writer = new BinaryWriter(_ms);
        }

        public void WriteString(string value)
        {
            if (value == null)
            {
                _writer.Write((uint)0);
            }
            else
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                _writer.Write((uint)bytes.Length);
                _writer.Write(bytes);
            }
        }

        public void WriteU32(uint value) => _writer.Write(value);
        public void WriteU64(ulong value) => _writer.Write(value);
        public void WriteF64(double value) => _writer.Write(value);
        public void WriteBool(bool value) => _writer.Write(value);
        public void WriteI32(int value) => _writer.Write(value);

        public byte[] ToArray() => _ms.ToArray();
    }

    public class MessageReader
    {
        private readonly MemoryStream _ms;
        private readonly BinaryReader _reader;

        public MessageReader(byte[] data)
        {
            _ms = new MemoryStream(data);
            _reader = new BinaryReader(_ms);
        }

        public string ReadString()
        {
            uint len = _reader.ReadUInt32();
            if (len == 0) return string.Empty;
            byte[] bytes = _reader.ReadBytes((int)len);
            return Encoding.UTF8.GetString(bytes);
        }

        public uint ReadU32() => _reader.ReadUInt32();
        public int ReadI32() => _reader.ReadInt32();
        public double ReadF64() => _reader.ReadDouble();
        public ulong ReadU64() => _reader.ReadUInt64();
    }

    public class SDKEngine
    {
        private static SDKEngine _instance;
        public static SDKEngine Instance => _instance ??= new SDKEngine();

        private IPCClient _ipcClient;
        private Process _serverProcess;

        public IPCClient IPC => _ipcClient;

        public async Task<bool> InitializeAsync(string pipeName = "OpenMediaSDK", string serverPath = "OpenMediaServer.exe")
        {
            // Try to connect first, in case it's already running
            _ipcClient = new IPCClient();
            bool connected = await _ipcClient.ConnectAsync(pipeName, 1000);

            if (!connected)
            {
                // Not running, launch it
                if (File.Exists(serverPath))
                {
                    _serverProcess = new Process();
                    _serverProcess.StartInfo.FileName = serverPath;

                    string? serverDir = Path.GetDirectoryName(serverPath);
                    if (!string.IsNullOrEmpty(serverDir))
                    {
                        _serverProcess.StartInfo.WorkingDirectory = serverDir;
                        string existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                        if (!existingPath.Contains(serverDir, StringComparison.OrdinalIgnoreCase))
                        {
                            _serverProcess.StartInfo.EnvironmentVariables["PATH"] = serverDir + ";" + existingPath;
                        }
                    }

                    if (pipeName != "OpenMediaSDK")
                    {
                        _serverProcess.StartInfo.Arguments = $"--pipe-name \\\\.\\pipe\\{pipeName}";
                    }
                    _serverProcess.StartInfo.UseShellExecute = false;
                    _serverProcess.StartInfo.CreateNoWindow = true; // Run in background
                    _serverProcess.Start();

                    // Try connecting again with longer timeout
                    connected = await _ipcClient.ConnectAsync(pipeName, 5000);
                }
            }

            if (!connected)
            {
                return false;
            }

            // Perform handshake
            var builder = new MessageBuilder();
            builder.WriteString("1.0.0-NET");
            byte[] response = await _ipcClient.SendAndReceiveAsync(CommandType.Handshake, builder.ToArray());
            
            if (response != null && response.Length > 0)
            {
                var reader = new MessageReader(response);
                string resStr = reader.ReadString();
                return resStr == "OK";
            }

            return false;
        }

        public async Task ShutdownAsync()
        {
            if (_ipcClient != null)
            {
                try
                {
                    // Send shutdown command gracefully to OpenMediaServer
                    await _ipcClient.ShutdownServerAsync();
                }
                catch {}
                _ipcClient.Dispose();
                _ipcClient = null;
            }

            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        if (!_serverProcess.WaitForExit(1500))
                        {
                            _serverProcess.Kill();
                        }
                    });
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }
                catch {}
            }
        }

        public void Shutdown()
        {
            try
            {
                ShutdownAsync().GetAwaiter().GetResult();
            }
            catch {}
        }
    }
}
