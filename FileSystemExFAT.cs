using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public class FileSystemExFAT : FileSystemFAT
    {
        #region ctor

        public FileSystemExFAT(IStorageIO storageIO)
            : base(storageIO)
        {
        }

        #endregion

        #region Public Methods

        FatEntry lastFileEntry;
        int entrySetCount = 0;

        public override IEnumerable<FatEntry> GetDirEntries(long startcluster = ROOT_DIR_FAT_12_16)
        {
            var bs = BootSector;
            var clusternum = startcluster;
            UpdateProgress(0);

            EventLog(string.Format("\r\nGetting Directory Entries => Partition: {0}, StartCluster: 0x{1:X8}, FATlocation: 0x{2:X8}, FATlength: {3} bytes",
                     bs.PartitionId, clusternum, bs.FatLocation, bs.FatSizeInBytes));
            var fatbytes = new byte[bs.FatSizeInBytes];
            _storageIO.Seek(bs.FatLocation);
            _storageIO.ReadBytes(fatbytes, fatbytes.Length);
            UpdateProgress(40);

            var progress = 0;
            var entryCount = 0;
            var fatentry = new List<FatEntry>();
            while (clusternum < bs.EofClusterFlagConst)
            {
                long entrylocation;
                if (clusternum == ROOT_DIR_FAT_12_16) entrylocation = bs.RootDirLocation;
                else entrylocation = bs.DataClusterLocation + GetClusterOffset(clusternum, bs.ClusterSizeInBytes);

                var entrydata = new byte[bs.ClusterSizeInBytes];
                _storageIO.Seek(entrylocation);
                _storageIO.ReadBytes(entrydata, entrydata.Length);
                EventLog(string.Format("Parsing Directory Entries => ClusterNum: 0x{0:X8}, Location: 0x{1:X8}", clusternum, entrylocation));
                UpdateProgress(40 + (progress += 10) % 60);

                for (int offset = 0; offset < bs.ClusterSizeInBytes; offset += FatEntry.Length)
                {
                    entryCount++;
                    var entryType = (FatEntryType)entrydata[offset];
                    EventLog(string.Format("{0:000} => EntryType: 0x{1:X2} -> {2}", entryCount, (byte)entryType, entryType));
                    if (entryType == FatEntryType.Null) break;

                    if (entryType == FatEntryType.ExFatVolumeLabel)
                    {
                        var exentry = ParseExFatVolumeLableEntry(entrydata, offset);
                        var entry = new FatEntry
                        {
                            Attribute = (int)EntryAttributes.VolumeLabel,
                            LongName = exentry.VolumeLabel,
                            LfnAvailable = true,
                            EntryIndex = entryCount,
                            Partition = this
                        };
                        fatentry.Add(entry);
                    }
                    else if (entryType == FatEntryType.AllocationBMP)
                    {
                        var exentry = ParseExFatAllocationBmpEntry(entrydata, offset);
                        var entry = new FatEntry
                        {
                            LongName = ((FatEntryType)exentry.EntryType).ToString(),
                            StartCluster = (int)exentry.FirstCluster,
                            FileSize = (int)exentry.DataLength,
                            LfnAvailable = true,
                            EntryIndex = entryCount,
                            Partition = this
                        };
                        fatentry.Add(entry);
                    }
                    else if (entryType == FatEntryType.UpCaseTable)
                    {
                        var exentry = ParseExFatUpCaseTableEntry(entrydata, offset);
                        var entry = new FatEntry
                        {
                            LongName = ((FatEntryType)exentry.EntryType).ToString(),
                            StartCluster = (int)exentry.FirstCluster,
                            FileSize = (int)exentry.DataLength,
                            LfnAvailable = true,
                            EntryIndex = entryCount,
                            Partition = this
                        };
                        fatentry.Add(entry);
                    }
                    else if (entryType == FatEntryType.ExFatFileDirectory)
                    {
                        lastFileEntry = new FatEntry();
                        lastFileEntry.LongName = "";
                        lastFileEntry.LfnAvailable = true;
                        lastFileEntry.EntryIndex = entryCount;
                        lastFileEntry.Partition = this;

                        var exentry = ParseExFatFileEntry(entrydata, offset);
                        lastFileEntry.Attribute = exentry.Attribute;
                        lastFileEntry.EntrySetCount = exentry.SecondaryCount;
                    }
                    else if (entryType == FatEntryType.StreamExt)
                    {
                        var streamExt = ParseExFatStreamExtEntry(entrydata, offset);
                        lastFileEntry.StartCluster = (int)streamExt.FirstCluster;
                        lastFileEntry.FileSize = (int)streamExt.DataLength;
                    }
                    else if (entryType == FatEntryType.ExFatFileName)
                    {
                        var lfnentry = ParseExFatFileNameEntry(entrydata, offset);
                        lastFileEntry.LongName += lfnentry.FileName;
                    }

                    if (lastFileEntry != null)
                    {
                        entrySetCount++;
                        if (entrySetCount >= lastFileEntry.EntrySetCount + 1)
                        {
                            fatentry.Add(lastFileEntry);
                            entrySetCount = 0;
                            lastFileEntry = null;
                        }
                    }
                }

                //get next cluster number
                if (bs.FsType == FsType.FAT12) clusternum = GetFAT12NextCluster(fatbytes, (int)clusternum);
                else clusternum = Num.ArrayToLong(fatbytes, clusternum * bs.BytesPerFatConst, bs.BytesPerFatConst);
                EventLog(string.Format("Parsing Directory Entries => NextCluster: 0x{0:X8}", clusternum));

                if (clusternum < ClusterNum.PreserveCount)
                {
                    EventLog("Invalid cluster number specified in chain");
                    break;
                }
                else if (clusternum == bs.BadClusterFlagConst) EventLog("Bad cluster mark found");
            }

            UpdateProgress(100);
            EventLog("Valid FAT Entry Found: " + fatentry.Count);
            return fatentry;
        }

        #endregion

        #region Static Methods

        private static ExFatVolumeLableEntry ParseExFatVolumeLableEntry(byte[] buffer, int offset)
        {
            var obj = new ExFatVolumeLableEntry
            {
                EntryType = buffer[offset + 0],
                CharacterCount = buffer[offset + 1],
                VolumeLabel = Encoding.Unicode.GetString(buffer, offset + 2, 22),
                Reserved = new byte[8]
            };
            Array.Copy(buffer, offset + 24, obj.Reserved, 0, obj.Reserved.Length);
            return obj;
        }

        private static ExFatAllocationBmpEntry ParseExFatAllocationBmpEntry(byte[] buffer, int offset)
        {
            var obj = new ExFatAllocationBmpEntry
            {
                EntryType = buffer[offset + 0],
                BitMapFlags = buffer[offset + 1],
                Reserved = new byte[18],
                FirstCluster = Num.ArrayToUInt(buffer, offset + 20, 4),
                DataLength = Num.ArrayToULong(buffer, offset + 24, 8),
            };
            Array.Copy(buffer, offset + 2, obj.Reserved, 0, obj.Reserved.Length);
            return obj;
        }


        private static ExFatUpCaseTableEntry ParseExFatUpCaseTableEntry(byte[] buffer, int offset)
        {
            var obj = new ExFatUpCaseTableEntry
            {
                EntryType = buffer[offset + 0],
                Reserved1 = new byte[3],
                TableChecksum = Num.ArrayToUInt(buffer, offset + 20, 4),
                Reserved2 = new byte[12],
                FirstCluster = Num.ArrayToUInt(buffer, offset + 20, 4),
                DataLength = Num.ArrayToULong(buffer, offset + 24, 8),
            };
            Array.Copy(buffer, offset + 1, obj.Reserved1, 0, obj.Reserved1.Length);
            Array.Copy(buffer, offset + 8, obj.Reserved2, 0, obj.Reserved2.Length);
            return obj;
        }

        private static ExFatFileDirEntry ParseExFatFileEntry(byte[] buffer, int offset)
        {
            var obj = new ExFatFileDirEntry
            {
                EntryType = buffer[offset + 0],
                SecondaryCount = buffer[offset + 1],
                SetCheckSum = Num.ArrayToInt(buffer, offset + 2, 2),
                Attribute = Num.ArrayToInt(buffer, offset + 4, 2),
                Reserved1 = Num.ArrayToInt(buffer, offset + 6, 2),
                CreateTimestamp = Num.ArrayToInt(buffer, offset + 8, 4),
                LastModifiedTimestamp = Num.ArrayToInt(buffer, offset + 12, 4),
                LastAccessedTimestamp = Num.ArrayToInt(buffer, offset + 16, 4),
                Create10msIncrement = buffer[offset + 20],
                LastModified10msIncrement = buffer[offset + 21],
                CreateTZOffset = buffer[offset + 22],
                LastModifiedTZOffset = buffer[offset + 23],
                LastAccessedTZOffset = buffer[offset + 24],
                Reserved2 = new byte[7],
            };
            Array.Copy(buffer, offset + 25, obj.Reserved2, 0, obj.Reserved2.Length);
            return obj;
        }

        private static ExFatStreamExtEntry ParseExFatStreamExtEntry(byte[] buffer, int offset)
        {
            var obj = new ExFatStreamExtEntry
            {
                EntryType = buffer[offset + 0],
                GeneralSecondaryFlags = buffer[offset + 1],
                Reserved1 = buffer[offset + 2],
                NameLength = buffer[offset + 3],
                NameHash = Num.ArrayToInt(buffer, offset + 4, 2),
                Reserved2 = Num.ArrayToInt(buffer, offset + 6, 2),
                ValidDataLength = Num.ArrayToULong(buffer, offset + 8, 8),
                Reserved3 = Num.ArrayToUInt(buffer, offset + 16, 4),
                FirstCluster = Num.ArrayToUInt(buffer, offset + 20, 4),
                DataLength = Num.ArrayToULong(buffer, offset + 24, 8),
            };
            return obj;
        }

        private static ExFatFileNameEntry ParseExFatFileNameEntry(byte[] buffer, int offset)
        {
            var obj = new ExFatFileNameEntry
            {
                EntryType = buffer[offset + 0],
                GeneralSecondaryFlags = buffer[offset + 1],
                FileName = Encoding.Unicode.GetString(buffer, offset + 2, 30)
            };
            return obj;
        }

        #endregion

    }

    #region ExFAT Specific Struct

    public class BootSectorExFAT
    {
        // exFAT specific
        public ulong PartitionOffset { get; set; }
        public ulong VolumeLength { get; set; }
        public uint FatOffset { get; set; }
        public uint FATsize { get; set; }
        public uint ClusterHeapOffset { get; set; }
        public uint ClusterCount { get; set; }
        public uint RootDirClusterNum { get; set; }
        public uint VolumeSerialNum { get; set; }
        public uint FsRevision { get; set; }
        public uint VolumeFlags { get; set; }
        public byte BytesPerSectorShift { get; set; }
        public byte SectorPerClusterShift { get; set; }
        public byte NumOfFat { get; set; }
        public byte DriveSelect { get; set; }
        public byte PercentInUse { get; set; }
        public byte[] Reserved { get; set; }
    }

    public class ExFatVolumeLableEntry
    {
        public byte EntryType { get; set; } //0x83
        public byte CharacterCount { get; set; }
        public string VolumeLabel { get; set; } //22 byte
        public byte[] Reserved { get; set; } //8 byte

        public const int Length = 32;
    }

    public class ExFatAllocationBmpEntry
    {
        public byte EntryType { get; set; } //0x81
        public byte BitMapFlags { get; set; }
        public byte[] Reserved { get; set; } //18 byte
        public uint FirstCluster { get; set; }
        public ulong DataLength { get; set; }

        public const int Length = 32;
    }

    public class ExFatUpCaseTableEntry
    {
        public byte EntryType { get; set; } //0x82
        public byte[] Reserved1 { get; set; }
        public uint TableChecksum { get; set; }
        public byte[] Reserved2 { get; set; } //12 byte
        public uint FirstCluster { get; set; }
        public ulong DataLength { get; set; }

        public const int Length = 32;
    }

    public class ExFatFileDirEntry
    {
        public byte EntryType { get; set; } //0x85
        public byte SecondaryCount { get; set; }
        public int SetCheckSum { get; set; }
        public int Attribute { get; set; }
        public int Reserved1 { get; set; } //2 byte
        public int CreateTimestamp { get; set; }
        public int LastModifiedTimestamp { get; set; }
        public int LastAccessedTimestamp { get; set; }
        public byte Create10msIncrement { get; set; }
        public byte LastModified10msIncrement { get; set; }
        public byte CreateTZOffset { get; set; }
        public byte LastModifiedTZOffset { get; set; }
        public byte LastAccessedTZOffset { get; set; }
        public byte[] Reserved2 { get; set; }  //7 byte

        public const int Length = 32;
    }

    public class ExFatStreamExtEntry
    {
        public byte EntryType { get; set; } //0xC0
        public byte GeneralSecondaryFlags { get; set; }
        public byte Reserved1 { get; set; }
        public byte NameLength { get; set; }
        public int NameHash { get; set; }
        public int Reserved2 { get; set; }
        public ulong ValidDataLength { get; set; }
        public uint Reserved3 { get; set; }
        public uint FirstCluster { get; set; }
        public ulong DataLength { get; set; }

        public const int Length = 32;
    }

    public class ExFatFileNameEntry
    {
        public byte EntryType { get; set; } //0xC1
        public byte GeneralSecondaryFlags { get; set; }
        public string FileName { get; set; } //30 byte

        public const int Length = 32;
    }

    #endregion
}
