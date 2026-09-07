using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OpenMedia.SDK.Interop
{
    /// <summary>
    /// Manages the lifetime of managed delegate callbacks passed to native code (resolves TD-01).
    /// Prevents premature garbage collection and 0xC0000005 (AccessViolationException).
    /// </summary>
    public static class CallbackLifetimeManager
    {
        private static readonly ConcurrentDictionary<IntPtr, List<GCHandle>> _pinnedHandles = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Roots and pins a delegate callback for the lifetime of the native owner handle.
        /// </summary>
        /// <param name="ownerHandle">The native pointer or SafeHandle dangerous address.</param>
        /// <param name="callback">The managed delegate to protect from GC collection.</param>
        public static void Register(IntPtr ownerHandle, Delegate? callback)
        {
            if (ownerHandle == IntPtr.Zero || callback == null)
                return;

            var gcHandle = GCHandle.Alloc(callback);

            _pinnedHandles.AddOrUpdate(
                ownerHandle,
                _ => new List<GCHandle> { gcHandle },
                (_, list) =>
                {
                    lock (_lock)
                    {
                        list.Add(gcHandle);
                    }
                    return list;
                });
        }

        /// <summary>
        /// Releases and unpins all callbacks associated with the given native owner handle.
        /// Should be called when the native resource is destroyed or disposed.
        /// </summary>
        /// <param name="ownerHandle">The native pointer that is being released.</param>
        public static void UnregisterAll(IntPtr ownerHandle)
        {
            if (ownerHandle == IntPtr.Zero)
                return;

            if (_pinnedHandles.TryRemove(ownerHandle, out var handles))
            {
                lock (_lock)
                {
                    foreach (var h in handles)
                    {
                        if (h.IsAllocated)
                        {
                            h.Free();
                        }
                    }
                    handles.Clear();
                }
            }
        }

        /// <summary>
        /// Scoped pin for one-shot or temporary delegate invocations.
        /// </summary>
        public static IDisposable PinTemporary(Delegate callback, out IntPtr functionPointer)
        {
            var handle = GCHandle.Alloc(callback);
            functionPointer = Marshal.GetFunctionPointerForDelegate(callback);
            return new DisposableHandle(handle);
        }

        private sealed class DisposableHandle : IDisposable
        {
            private GCHandle _handle;
            private bool _disposed;

            public DisposableHandle(GCHandle handle)
            {
                _handle = handle;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    if (_handle.IsAllocated)
                    {
                        _handle.Free();
                    }
                    _disposed = true;
                }
            }
        }
    }
}
