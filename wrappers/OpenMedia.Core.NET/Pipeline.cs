using System;
using System.Threading.Tasks;

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

    public class Pipeline : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed = false;

        private NativeBridge.StateChangedCallback _stateCallbackDelegate;
        private NativeBridge.ErrorCallback _errorCallbackDelegate;

        public event EventHandler<PipelineState> StateChanged;
        public event EventHandler<PipelineErrorEventArgs> Error;

        public Pipeline()
        {
            _handle = NativeBridge.ome_pipeline_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create pipeline.");

            // Register callbacks
            _stateCallbackDelegate = new NativeBridge.StateChangedCallback(OnStateChangedInternal);
            _errorCallbackDelegate = new NativeBridge.ErrorCallback(OnErrorInternal);
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

        public Task<bool> StartAsync()
        {
            // Tương lai: dùng TaskCompletionSource lắng nghe event StateChanged(Running).
            // Hiện tại dùng Task.Run để tránh lock main UI thread.
            return Task.Run(() => Start());
        }

        public bool Stop()
        {
            return NativeBridge.ome_pipeline_stop(_handle);
        }

        public Task<bool> StopAsync()
        {
            return Task.Run(() => Stop());
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
                if (_handle != IntPtr.Zero)
                {
                    // Unregister callbacks before destroying
                    NativeBridge.ome_pipeline_set_state_callback(_handle, null);
                    NativeBridge.ome_pipeline_set_error_callback(_handle, null);

                    NativeBridge.ome_pipeline_destroy(_handle);
                    _handle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~Pipeline()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
