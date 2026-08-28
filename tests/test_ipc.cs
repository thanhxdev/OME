using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

class Program
{
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

    static async Task Main(string[] args)
    {
        var pipe = new NamedPipeClientStream(".", "OpenMediaIPC", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync();
        Console.WriteLine("Connected");

        // 1. Create pipeline
        byte[] req1 = CreateCommand(0x0100, CreatePipeline("Player_1", 1920, 1080, 60.0));
        await pipe.WriteAsync(req1, 0, req1.Length);
        var resp1 = await ReadResponse(pipe);
        uint pipelineId = BitConverter.ToUInt32(resp1, 0);
        Console.WriteLine("Pipeline ID: " + pipelineId);

        // 2. Open source
        byte[] req2 = CreateCommand(0x0200, OpenSource(pipelineId, 1, @"c:\Users\ASUS NUC\Desktop\Code\OME\tests\data\sample.mp4"));
        await pipe.WriteAsync(req2, 0, req2.Length);
        await ReadResponse(pipe);
        Console.WriteLine("Opened source");

        // 3. GetSourceInfo
        byte[] req3 = CreateCommand(0x0203, GetSourceInfo(pipelineId, 1));
        await pipe.WriteAsync(req3, 0, req3.Length);
        var resp3 = await ReadResponse(pipe);
        Console.WriteLine("GetSourceInfo Response Size: " + resp3.Length);

        // Parse it manually to print raw values
        ParseSourceInfo(resp3);
    }

    static void ParseSourceInfo(byte[] data)
    {
        int offset = 0;
        uint strLen = BitConverter.ToUInt32(data, offset); offset += 4;
        string url = Encoding.UTF8.GetString(data, offset, (int)strLen); offset += (int)strLen;
        double duration = BitConverter.ToDouble(data, offset); offset += 8;
        uint w = BitConverter.ToUInt32(data, offset); offset += 4;
        uint h = BitConverter.ToUInt32(data, offset); offset += 4;
        double fps = BitConverter.ToDouble(data, offset); offset += 8;
        
        Console.WriteLine("URL: " + url + ", Duration: " + duration + ", Width: " + w + ", Height: " + h + ", FPS: " + fps);
    }

    static byte[] CreateCommand(uint cmdType, byte[] payload)
    {
        byte[] header = new byte[40];
        BitConverter.GetBytes(0x4F4D4549).CopyTo(header, 0); // Magic
        BitConverter.GetBytes((ushort)1).CopyTo(header, 4);  // Version
        BitConverter.GetBytes(cmdType).CopyTo(header, 8);    // CommandType
        BitConverter.GetBytes((uint)1).CopyTo(header, 12);   // Sequence
        BitConverter.GetBytes((uint)payload.Length).CopyTo(header, 16); // Size
        BitConverter.GetBytes((ulong)0).CopyTo(header, 24);  // Timestamp
        BitConverter.GetBytes((uint)0).CopyTo(header, 32);   // ClientId

        byte[] all = new byte[40 + payload.Length];
        header.CopyTo(all, 0);
        payload.CopyTo(all, 40);
        return all;
    }

    static byte[] CreatePipeline(string name, uint w, uint h, double fps)
    {
        var b = new System.Collections.Generic.List<byte>();
        var strBytes = Encoding.UTF8.GetBytes(name);
        b.AddRange(BitConverter.GetBytes((uint)strBytes.Length));
        b.AddRange(strBytes);
        b.AddRange(BitConverter.GetBytes(w));
        b.AddRange(BitConverter.GetBytes(h));
        b.AddRange(BitConverter.GetBytes(fps));
        return b.ToArray();
    }

    static byte[] OpenSource(uint pId, uint sId, string url)
    {
        var b = new System.Collections.Generic.List<byte>();
        b.AddRange(BitConverter.GetBytes(pId));
        b.AddRange(BitConverter.GetBytes(sId));
        var strBytes = Encoding.UTF8.GetBytes(url);
        b.AddRange(BitConverter.GetBytes((uint)strBytes.Length));
        b.AddRange(strBytes);
        b.Add(0); // loop false
        b.AddRange(BitConverter.GetBytes((uint)0)); // startMs
        return b.ToArray();
    }

    static byte[] GetSourceInfo(uint pId, uint sId)
    {
        var b = new System.Collections.Generic.List<byte>();
        b.AddRange(BitConverter.GetBytes(pId));
        b.AddRange(BitConverter.GetBytes(sId));
        return b.ToArray();
    }

    static async Task<byte[]> ReadResponse(NamedPipeClientStream pipe)
    {
        byte[] header = new byte[32]; // sizeof ResponseHeader in C#
        await pipe.ReadExactlyAsync(header, 0, 32);
        uint status = BitConverter.ToUInt32(header, 8);
        uint size = BitConverter.ToUInt32(header, 16);
        if (status != 0) throw new Exception("Status != 0");
        byte[] payload = new byte[size];
        if (size > 0) await pipe.ReadExactlyAsync(payload, 0, (int)size);
        return payload;
    }
}
