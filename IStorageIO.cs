using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public interface IStorageIO : IDisposable
    {
        void Close();
        void Seek(long offset, SeekOrigin seekOrigin = SeekOrigin.Begin);
        void ReadBytes(byte[] data, int length);
        void WriteBytes(byte[] data, int length);
    }
}
