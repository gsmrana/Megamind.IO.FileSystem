using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public class DiskImageIO : IStorageIO
    {
        readonly FileStream _filestream;

        public DiskImageIO(string filename)
        {
            _filestream = new FileStream(filename, FileMode.Open, FileAccess.ReadWrite);
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
            _filestream.Seek(offset, SeekOrigin.Begin);
        }

        public void ReadBytes(byte[] data, int length)
        {
            _filestream.Read(data, 0, length);
        }

        public void WriteBytes(byte[] data, int length)
        {
            _filestream.Write(data, 0, length);
        }
    }
}
