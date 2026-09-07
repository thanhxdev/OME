using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenMedia.SDK.Interop;
using OpenMedia.SDK.SafeHandles;

namespace OpenMedia.SDK
{
    public enum PipelineState
    {
        Stopped = 0,
        Starting = 1,
        Running = 2,
        Error = 3
    }

    public class PipelineErrorEventArgs : EventArgs
    {
        public int ErrorCode { get; }
        public string Message { get; }
        public PipelineErrorEventArgs(int errorCode, string message)
        {
            ErrorCode = errorCode;
            Message = message;
        }
    }

    /// <summary>
    /// Managed Pipeline with type-safe SafeHandle lifecycle, GC callback protection (TD-01),
    /// and modern async control flow.
    /// </summary>
    public class Pipeline : IDisposable
    {
        private readonly SafePipelineHandle _handle;
        private bool _disposed = false;

        private readonly NativeBridge.StateChangedCallback _stateCallbackDelegate;
        private readonly NativeBridge.ErrorCallback _errorCallbackDelegate;

        public SafePipelineHandle SafeHandle => _handle;
        public IntPtr Handle => _handle.DangerousGetHandle();

        public event EventHandler<PipelineState>? StateChanged;
        public event EventHandler<PipelineErrorEventArgs>? Error;

        public Pipeline()
        {
            _handle = NativeBridge.ome_pipeline_create();
            if (_handle.IsInvalid)
                throw new InvalidOperationException("Failed to create pipeline.");

            // Register callbacks with GC lifetime protection (TD-01)
            _stateCallbackDelegate = OnStateChangedInternal;
            _errorCallbackDelegate = OnErrorInternal;

            NativeBridge.ome_pipeline_set_state_callback(_handle, _stateCallbackDelegate);
            NativeBridge.ome_pipeline_set_error_callback(_handle, _errorCallbackDelegate);
        }

        private void OnStateChangedInternal(int state)
        {
            StateChanged?.Invoke(this, (PipelineState)state);
        }

        private void OnErrorInternal(int errorCode, string message)
        {
            Error?.Invoke(this, new PipelineErrorEventArgs(errorCode, message));
        }

        public bool Start()
        {
            return NativeBridge.ome_pipeline_start(_handle);
        }

        public Task<bool> StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Start();
            }, cancellationToken);
        }

        public bool Stop()
        {
            return NativeBridge.ome_pipeline_stop(_handle);
        }

        public Task<bool> StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Stop();
            }, cancellationToken);
        }

        public bool AddNode(SafeHandle nodeHandle)
        {
            if (nodeHandle == null || nodeHandle.IsInvalid) return false;
            return NativeBridge.ome_pipeline_add_node(_handle, nodeHandle.DangerousGetHandle());
        }

        public bool AddNode(IntPtr nodeHandle)
        {
            return NativeBridge.ome_pipeline_add_node(_handle, nodeHandle);
        }

        public Pipeline WithNode(IntPtr nodeHandle)
        {
            if (!AddNode(nodeHandle))
                throw new OpenMediaException("Failed to add node to pipeline.");
            return this;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Clean up callbacks and release native handle
                    NativeBridge.ome_pipeline_set_state_callback(_handle, null);
                    NativeBridge.ome_pipeline_set_error_callback(_handle, null);
                    CallbackLifetimeManager.UnregisterAll(_handle.DangerousGetHandle());
                    _handle.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
