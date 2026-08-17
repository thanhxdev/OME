using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenMedia.SDK
{
    public static class NativeStringHelper
    {
        public static IntPtr StringToUtf8Pointer(string managedString)
        {
            if (managedString == null) return IntPtr.Zero;
            
            int byteCount = Encoding.UTF8.GetByteCount(managedString);
            IntPtr buffer = Marshal.AllocHGlobal(byteCount + 1);
            
            byte[] bytes = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(managedString, 0, managedString.Length, bytes, 0);
            bytes[byteCount] = 0; // Null terminator
            
            Marshal.Copy(bytes, 0, buffer, byteCount + 1);
            return buffer;
        }

        public static string Utf8PointerToString(IntPtr nativeUtf8)
        {
            if (nativeUtf8 == IntPtr.Zero) return null;
            
            int length = 0;
            while (Marshal.ReadByte(nativeUtf8, length) != 0)
            {
                length++;
            }
            
            if (length == 0) return string.Empty;
            
            byte[] buffer = new byte[length];
            Marshal.Copy(nativeUtf8, buffer, 0, length);
            
            return Encoding.UTF8.GetString(buffer);
        }

        public static void FreeUtf8Pointer(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
}
