using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public interface IFileSystemBase
    {
        IEnumerable<FatEntry> GetDirEntries(long startcluster);
        void ReadFile(FatEntry file, Stream dest);
        void WriteFile(FatEntry file, Stream source);
        FatEntry CreateFile(FatEntry directory);
        void DeleteFile(FatEntry file);
        void Format(int sectorsize);
    }
}
