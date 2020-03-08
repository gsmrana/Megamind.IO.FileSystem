using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public static class NativeMethods
    {
        #region Win32 API

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern bool SetFilePointerEx(
            SafeFileHandle hFile,
            long liDistanceToMove,
            ref long lpNewFilePointer,
            uint dwMoveMethod
            );

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
            );

        [DllImport("kernel32", SetLastError = true)]
        internal static extern int ReadFile(
            SafeFileHandle handle,
            byte[] bytes,
            int numBytesToRead,
            out int numBytesRead,
            IntPtr overlapped_MustBeZero
            );

        #endregion
    }
}
