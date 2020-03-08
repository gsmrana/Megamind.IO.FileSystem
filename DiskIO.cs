using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public class DiskIO : IStorageIO
    {
        #region Const

        const int SectorSize = 512;

        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint CREATE_NEW = 1;
        const uint CREATE_ALWAYS = 2;
        const uint OPEN_EXISTING = 3;

        #endregion

        readonly SafeFileHandle _filestream;

        public DiskIO(string filename)
        {
            _filestream = NativeMethods.CreateFile(filename, GENERIC_READ, 3, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (_filestream.IsInvalid) Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        public void Close()
        {
            _filestream.Close();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) _filestream.Dispose();
        }

        public void Seek(long offset, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            long newptr = 0;
            if (offset % SectorSize != 0) throw new Exception("Seek Error: Offset should be multiple of media SectorSize " + SectorSize);
            NativeMethods.SetFilePointerEx(_filestream, offset, ref newptr, (uint)seekOrigin);
        }

        public void ReadBytes(byte[] data, int length)
        {
            if (length % SectorSize != 0) throw new Exception("Read Error: Length should be multiple of media SectorSize " + SectorSize);
            NativeMethods.ReadFile(_filestream, data, length, out int readcount, IntPtr.Zero);
        }

        public void WriteBytes(byte[] data, int length)
        {
            throw new NotImplementedException();
        }
    }

    public class DiskInfo
    {
        public string FileName { get; set; }
        public string Model { get; set; }
        public string PartitionCount { get; set; }
        public string BytesPerSector { get; set; }
        public string TotalSectors { get; set; }
        public string TotalSize { get; set; }
        public string Description { get; set; }
        public string InterfaceType { get; set; }
        public string MediaType { get; set; }
        public string SerialNumber { get; set; }
    }
}
