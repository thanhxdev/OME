using System;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    /// <summary>
    /// Represents a zero-copy direct memory view of a native media frame plane.
    /// Compatible with async streams (IAsyncEnumerable) and high-performance pipeline buffers.
    /// </summary>
    public readonly struct MediaFrameSpan
    {
        public IntPtr DataPointer { get; }
        public int Length { get; }
        public int Stride { get; }
        public int Width { get; }
        public int Height { get; }
        public PixelFormat Format { get; }
        public long TimestampUs { get; }
        public SafeMediaFrameHandle? FrameHandle { get; }

        public bool IsEmpty => DataPointer == IntPtr.Zero || Length <= 0;

        public MediaFrameSpan(
            IntPtr dataPointer,
            int length,
            int stride,
            int width,
            int height,
            PixelFormat format,
            long timestampUs = 0,
            SafeMediaFrameHandle? frameHandle = null)
        {
            DataPointer = dataPointer;
            Length = length;
            Stride = stride;
            Width = width;
            Height = height;
            Format = format;
            TimestampUs = timestampUs;
            FrameHandle = frameHandle;
        }

        /// <summary>
        /// Gets a zero-copy read-only span wrapping the native plane memory.
        /// </summary>
        public unsafe ReadOnlySpan<byte> Span =>
            DataPointer == IntPtr.Zero || Length <= 0
                ? ReadOnlySpan<byte>.Empty
                : new ReadOnlySpan<byte>((void*)DataPointer, Length);

        /// <summary>
        /// Gets a zero-copy writable span wrapping the native plane memory.
        /// </summary>
        public unsafe Span<byte> AsWritableSpan() =>
            DataPointer == IntPtr.Zero || Length <= 0
                ? Span<byte>.Empty
                : new Span<byte>((void*)DataPointer, Length);

        /// <summary>
        /// Copies data into an isolated managed byte array if persistence across frame lifetimes is required.
        /// </summary>
        public byte[] ToArray()
        {
            if (IsEmpty) return Array.Empty<byte>();
            return Span.ToArray();
        }
    }
}
