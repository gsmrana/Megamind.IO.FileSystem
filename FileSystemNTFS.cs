using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public class FileSystemNTFS : FileSystemBase
    {
        #region Const and Data

        const int MFT_FILE_COUNT = 12;
        const int MFT_FILE_SIZE = 1024;

        public List<NtfsSysFileInfo> SystemFiles = new List<NtfsSysFileInfo>();

        #endregion

        #region ctor

        public FileSystemNTFS(IStorageIO storageIO)
            : base(storageIO)
        {
            SystemFiles = new List<NtfsSysFileInfo>();
            var sysfiles = Enum.GetValues(typeof(NtfsSysFiles));
            for (int i = 0; i < MFT_FILE_COUNT; i++)
            {
                var sysfile = new NtfsSysFileInfo
                {
                    Index = i,
                    File = (NtfsSysFiles)sysfiles.GetValue(i),
                    FileName = "$" + sysfiles.GetValue(i),
                    Size = MFT_FILE_SIZE,
                };
                SystemFiles.Add(sysfile);
            }
        }

        #endregion

        #region Public Methods

        public override IEnumerable<FatEntry> GetDirEntries(long startcluster = ROOT_DIR_FAT_12_16)
        {
            var bs = BootSector;
            var ntfsbs = bs.NtfsBS.First();
            var clusternum = startcluster;
            UpdateProgress(0);

            EventLog(string.Format("\r\nGetting Directory Entries => Partition: {0}, StartCluster: 0x{1:X8}, $MFT ClusterNum: 0x{2:X8}, $MFTMIRR ClusterNum: 0x{3:X8}",
                     bs.PartitionId, clusternum, ntfsbs.MftFileClusterNum, ntfsbs.MftMirrFileClusterNum));

            SystemFiles.FirstOrDefault(p => p.File == NtfsSysFiles.Mft).ClusterNum = ntfsbs.MftFileClusterNum;
            SystemFiles.FirstOrDefault(p => p.File == NtfsSysFiles.MftMirr).ClusterNum = ntfsbs.MftMirrFileClusterNum;

            var bytes2read = MFT_FILE_COUNT * MFT_FILE_SIZE;
            var location = (bs.MbrPartitionTable.First().StartSector + ((long)ntfsbs.MftFileClusterNum * bs.SectorPerCluster)) * bs.BytesPerSector;
            var buffer = new byte[bytes2read];
            EventLog(string.Format("ReadSystemFile => ClusterNum: 0x{0:X8}, Location: 0x{1:X8}, Size: 0x{2:X8}", ntfsbs.MftFileClusterNum, location, bytes2read));
            _storageIO.Seek(location);
            _storageIO.ReadBytes(buffer, buffer.Length);
            for (int i = 0; i < MFT_FILE_COUNT; i++)
            {
                SystemFiles[i].MftEntry = ParseMftEntry(buffer, i * MFT_FILE_SIZE);
            }

            var fatentry = new List<FatEntry>();
            foreach (var sysfile in SystemFiles)
            {
                var entry = new FatEntry
                {
                    LongName = sysfile.FileName,
                    LfnAvailable = true,
                    Attribute = (int)EntryAttributes.SystemFile,
                    StartCluster = (int)sysfile.ClusterNum,
                    FileSize = (int)sysfile.Size,
                    EntryIndex = sysfile.Index,
                };
                fatentry.Add(entry);
            }
            return fatentry;
        }

        public override void ReadFile(FatEntry file, Stream dest)
        {
            var bs = BootSector;
            var clusternum = file.StartCluster;
            var bytes2read = file.FileSize;
            UpdateProgress(0);

            var location = (bs.MbrPartitionTable.First().StartSector + (clusternum * bs.SectorPerCluster)) * (long)bs.BytesPerSector;
            EventLog(string.Format("ReadFile => ClusterNum: 0x{0:X8}, Location: 0x{1:X8}, Size: 0x{2:X8}", clusternum, location, bytes2read));

            var buffer = new byte[bytes2read];
            _storageIO.Seek(location);
            _storageIO.ReadBytes(buffer, buffer.Length);
            dest.Write(buffer, 0, buffer.Length);
            dest.Close();

            //var mft = ParseMftEntry(buffer);

            UpdateProgress(100);
            EventLog(string.Format("ReadFile Completed: {0} bytes", file.FileSize - bytes2read));
        }

        #endregion

        #region Static Methods

        public static MftEntry ParseMftEntry(byte[] buffer, int offset)
        {
            var obj = new MftEntry
            {
                Signature = Encoding.ASCII.GetString(buffer, offset + 0, 4),
                Offset = Num.ArrayToInt(buffer, offset + 4, 2),
                NumberOfEntry = Num.ArrayToInt(buffer, offset + 6, 2),
                LogFileSeqNum = new byte[8],
                SequenceValue = Num.ArrayToInt(buffer, offset + 16, 2),
                LinkCount = Num.ArrayToInt(buffer, offset + 18, 2),
                OffsetToFirstAttribute = Num.ArrayToInt(buffer, offset + 20, 2),
                Flags = Num.ArrayToInt(buffer, offset + 22, 2),
                UsedSizeOfMftEntry = Num.ArrayToUInt(buffer, offset + 24, 4),
                AllocatedSizeOfMftEntry = Num.ArrayToUInt(buffer, offset + 28, 4),
                FileReferenceToBaseRecord = Num.ArrayToULong(buffer, offset + 32, 8),
                NextAtributeIdentifier = Num.ArrayToInt(buffer, offset + 40, 2),
                AtributesAndFixupValues = new byte[982],
            };
            Array.Copy(buffer, offset + 8, obj.LogFileSeqNum, 0, obj.LogFileSeqNum.Length);
            Array.Copy(buffer, offset + 42, obj.AtributesAndFixupValues, 0, obj.AtributesAndFixupValues.Length);
            return obj;
        }

        #endregion

    }

    #region NTFS Specific Struct

    public enum NtfsSysFiles
    {
        Mft,
        MftMirr,
        LogFile,
        Volume,
        AttrDef,
        RootDir,
        Bitmap,
        Boot,
        BadClus,
        Quota,
        UpCase,
        Extend,
        Reparse,
        UsnJrnl
    }

    public class NtfsSysFileInfo
    {
        public int Index { get; set; }
        public NtfsSysFiles File { get; set; }
        public string FileName { get; set; }
        public long ClusterNum { get; set; }
        public long Size { get; set; }
        public MftEntry MftEntry { get; set; }
    }

    public class BootSectorNTFS
    {
        public ulong TotalSectors { get; set; }
        public long MftFileClusterNum { get; set; }
        public long MftMirrFileClusterNum { get; set; }
        public int ClustersPerFileRecordSegment { get; set; }
        public byte ClusterPerIndexBuffer { get; set; }
        public byte[] VolumeSerial { get; set; } //8 byte
        public int Checksum { get; set; }
    }

    public class MftEntry
    {
        public string Signature { get; set; } //4 byte
        public int Offset { get; set; }
        public int NumberOfEntry { get; set; }
        public byte[] LogFileSeqNum { get; set; } //8 byte
        public int SequenceValue { get; set; }
        public int LinkCount { get; set; }
        public int OffsetToFirstAttribute { get; set; }
        public int Flags { get; set; }
        public uint UsedSizeOfMftEntry { get; set; }
        public uint AllocatedSizeOfMftEntry { get; set; }
        public ulong FileReferenceToBaseRecord { get; set; }
        public int NextAtributeIdentifier { get; set; }
        public byte[] AtributesAndFixupValues { get; set; } //982 byte

        public const int Length = 42;
        public const string GoodEntrySignature = "FILE";
        public const string BadEntrySignature = "BAAD";
    }

    #endregion

}
