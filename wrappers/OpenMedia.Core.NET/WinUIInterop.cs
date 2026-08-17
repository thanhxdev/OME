using System;
using System.Runtime.InteropServices;

namespace OpenMedia.SDK
{
    public static class WinUIInterop
    {
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
        public interface ISwapChainPanelNative
        {
            void SetSwapChain(IntPtr swapChain);
        }

        public static void SetSwapChain(object swapChainPanel, IntPtr swapChainPtr)
        {
            if (swapChainPanel == null) throw new ArgumentNullException(nameof(swapChainPanel));

            if (swapChainPanel is ISwapChainPanelNative panelNative)
            {
                panelNative.SetSwapChain(swapChainPtr);
                return;
            }

            // Direct COM QueryInterface for WinUI 3 (supports CsWinRT wrappers)
            IntPtr pUnknown = IntPtr.Zero;
            try
            {
                pUnknown = Marshal.GetIUnknownForObject(swapChainPanel);
                if (pUnknown != IntPtr.Zero)
                {
                    Guid iid = typeof(ISwapChainPanelNative).GUID;
                    if (Marshal.QueryInterface(pUnknown, ref iid, out IntPtr pNative) == 0 && pNative != IntPtr.Zero)
                    {
                        try
                        {
                            var comObj = (ISwapChainPanelNative)Marshal.GetObjectForIUnknown(pNative);
                            comObj.SetSwapChain(swapChainPtr);
                            return;
                        }
                        finally
                        {
                            Marshal.Release(pNative);
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (pUnknown != IntPtr.Zero)
                {
                    Marshal.Release(pUnknown);
                }
            }

            throw new ArgumentException("The provided object does not implement ISwapChainPanelNative.", nameof(swapChainPanel));
        }
    }
}
