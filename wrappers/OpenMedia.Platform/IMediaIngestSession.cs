using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.Platform.Models;
using OpenMedia.SDK;

namespace OpenMedia.Platform
{
    /// <summary>
    /// Unified modern broadcast ingest session interface supporting async streams (IAsyncEnumerable)
    /// and zero-copy frame access (MediaFrameSpan).
    /// </summary>
    public interface IMediaIngestSession : IAsyncDisposable
    {
        ValueTask<bool> StartAsync(CancellationToken ct = default);
        ValueTask StopAsync();
        IAsyncEnumerable<MediaFrameSpan> ReadFramesAsync(CancellationToken ct = default);
        StreamStatistics CurrentStats { get; }
    }
}
