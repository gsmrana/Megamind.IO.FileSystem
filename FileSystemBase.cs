using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    #region Delegates

    public delegate void ProgessEventHandler(object sender, ProgressUpdateEventArgs e);
    public delegate void LogEventHandler(object sender, LogEventArgs e);

    #endregion

    #region EventArgs

    public class ProgressUpdateEventArgs : EventArgs
    {
        public int Percent { get; set; }
    }

    public class LogEventArgs : EventArgs
    {
        public string Message { get; set; }
    }

    #endregion

    public class FileSystemBase : IFileSystemBase
    {
        #region Data

        public const int ROOT_DIR_FAT_12_16 = 0;
        public const int DEFAULT_SECT_SIZE = 512;

        protected IStorageIO _storageIO;
        public BootSectorCommon BootSector { get; set; }
        public bool IncludeDeletedEntry { get; set; }

        #endregion

        #region ctor

        public FileSystemBase(IStorageIO storageIO)
        {
            _storageIO = storageIO;
        }

        #endregion

        #region Event Handler

        public event LogEventHandler OnEventLog;
        public event ProgessEventHandler OnProgress;

        public virtual void EventLog(string message)
        {
            OnEventLog?.Invoke(this, new LogEventArgs { Message = message });
        }

        public virtual void UpdateProgress(int percent)
        {
            OnProgress?.Invoke(this, new ProgressUpdateEventArgs { Percent = percent });
        }

        #endregion

        #region Static Methods

        public static int GetFAT12NextCluster(byte[] fattable, int currentcluster)
        {
            var nextcluster = 0;
            var index = (currentcluster * 3) / 2;
            if ((currentcluster % 2) == 0) //even
            {
                nextcluster += fattable[index];
                nextcluster += (fattable[index + 1] & 0x0F) << 8;
            }
            else //odd
            {
                nextcluster += (fattable[index] & 0xF0) >> 4;
                nextcluster += fattable[index + 1] << 4;
            }
            return nextcluster;
        }

        public static long GetClusterOffset(long clustetnum, long clustersize)
        {
            if (clustetnum < ClusterNum.PreserveCount) throw new Exception("Invalid Cluster Number");
            return (clustetnum - ClusterNum.PreserveCount) * clustersize;
        }

        public static MasterBootRecord ParseMBR(byte[] buffer)
        {
            var mbr = new MasterBootRecord();

            //copy raw data bytes
            mbr.RawBytes = new byte[buffer.Length];
            Array.Copy(buffer, mbr.RawBytes, mbr.RawBytes.Length);

            //copy bootcode
            mbr.BootCode = new byte[MasterBootRecord.BootCodeSize];
            Array.Copy(buffer, 0, mbr.BootCode, 0, mbr.BootCode.Length);

            //parse drive serial and protection status
            mbr.DriveSerialNo = Num.ArrayToHexString(buffer, MasterBootRecord.BootCodeSize, 4);
            mbr.CopyProtectionFlag = Num.ArrayToHexString(buffer, MasterBootRecord.BootCodeSize + 4, 2);

            //copy signature
            mbr.Signature = Num.ArrayToHexString(buffer, 510, 2);

            //parse MBR partition table
            mbr.PartitionTable = new List<MbrPartitionTable>();
            for (int i = 0; i < MasterBootRecord.MaxPartition; i++)
            {
                var pdata = new byte[MbrPartitionTable.Length];
                Array.Copy(buffer, MasterBootRecord.PartitionTableIndex + MbrPartitionTable.Length * i, pdata, 0, pdata.Length);
                var ps = new MbrPartitionTable
                {
                    BootFlag = Num.ArrayToHexString(pdata, 0, 1),
                    ChsBegin = Num.ArrayToHexString(pdata, 1, 3),
                    FileSystemMbr = Num.ArrayToInt(pdata, 4, 1),
                    ChsEnd = Num.ArrayToHexString(pdata, 5, 3),
                    StartSector = Num.ArrayToInt(pdata, 8, 4),
                    TotalSectors = Num.ArrayToInt(pdata, 12, 4)
                };

                //file system validity check             
                if (ps.FileSystemMbr >= (int)FsTypesMbr.FAT12_CHS &&
                    ps.FileSystemMbr <= (int)FsTypesMbr.Extended_LBA &&
                    ps.StartSector > 0 && ps.TotalSectors > 0)
                {
                    mbr.PartitionTable.Add(ps);
                }
                else if (ps.FileSystemMbr == (int)FsTypesMbr.GptParitionSignature)
                {
                    mbr.PartitionTable.Add(ps);
                }
            }

            return mbr;
        }

        // not reliable
        private static FsType DetectFSTypeFromPT(FsTypesMbr mbrpt)
        {
            var fs = FsType.Unknown;
            if (mbrpt == FsTypesMbr.FAT12_CHS)
                fs = FsType.FAT12;
            else if (mbrpt == FsTypesMbr.FAT16_CHS)
                fs = FsType.FAT16;
            else if (mbrpt == FsTypesMbr.FAT32_CHS ||
                     mbrpt == FsTypesMbr.FAT32_LBA)
                fs = FsType.FAT32;
            return fs;
        }

        public static BootSectorCommon ParseBootSector(MbrPartitionTable pt, byte[] buffer)
        {
            var bscmn = new BootSectorCommon();
            bscmn.FsType = FsType.Unknown;

            //copy raw data bytes
            bscmn.RawBytes = new byte[buffer.Length];
            Array.Copy(buffer, bscmn.RawBytes, bscmn.RawBytes.Length);

            //add parition table
            bscmn.MbrPartitionTable = new List<MbrPartitionTable>();
            bscmn.MbrPartitionTable.Add(pt);

            //parse common properties
            bscmn.JumpCode = Num.ArrayToHexString(buffer, 0, 3);
            bscmn.OemName = Encoding.ASCII.GetString(buffer, 3, 8);
            bscmn.BytesPerSector = Num.ArrayToInt(buffer, 11, 2);
            bscmn.SectorPerCluster = Num.ArrayToInt(buffer, 13, 1);
            bscmn.ResvSectorCount = Num.ArrayToInt(buffer, 14, 2);
            bscmn.NumOfFat = Num.ArrayToInt(buffer, 16, 1);
            bscmn.RootEntryCount = Num.ArrayToInt(buffer, 17, 2);
            bscmn.TotalSector16 = Num.ArrayToInt(buffer, 19, 2);
            bscmn.MediaType = Num.ArrayToInt(buffer, 21, 1);
            bscmn.FatSizeInSector = Num.ArrayToInt(buffer, 22, 2);
            bscmn.SectorPerTrack = Num.ArrayToInt(buffer, 24, 2);
            bscmn.NumOfHeads = Num.ArrayToInt(buffer, 26, 2);
            bscmn.HiddenSectorCount = Num.ArrayToLong(buffer, 28, 4);
            bscmn.TotalSector32 = Num.ArrayToLong(buffer, 32, 4);
            bscmn.BootCode = new byte[BootSectorCommon.BootCodeLength];
            Array.Copy(buffer, 62, bscmn.BootCode, 0, bscmn.BootCode.Length);
            bscmn.Signature = Num.ArrayToHexString(buffer, 510, 2);
            bscmn.TotalSectors = bscmn.TotalSector16 != 0 ? bscmn.TotalSector16 : bscmn.TotalSector32;

            //try detect FS exFAT or NTFS from OEM name
            if (bscmn.FsType == FsType.Unknown)
            {
                if (bscmn.OemName.Contains("EXFAT")) bscmn.FsType = FsType.ExFAT;
                else if (bscmn.OemName.Contains("NTFS")) bscmn.FsType = FsType.NTFS;
            }

            //try detect FS from total cluster count
            if (bscmn.FsType == FsType.Unknown)
            {
                if (bscmn.SectorPerCluster > 0 && bscmn.TotalSectors > 0)
                {
                    var clscount = bscmn.TotalSectors / bscmn.SectorPerCluster;
                    if (clscount >= ClusterNum.BadFAT16) bscmn.FsType = FsType.FAT32;
                    else if (clscount >= ClusterNum.BadFAT12) bscmn.FsType = FsType.FAT16;
                    else if (clscount > 0) bscmn.FsType = FsType.FAT12;
                }
            }

            //parse FS specific properties
            if (bscmn.FsType == FsType.ExFAT)
            {
                var exfat = new BootSectorExFAT();
                bscmn.ExFatBS = new List<BootSectorExFAT>();
                bscmn.ExFatBS.Add(exfat);

                //parse exFAT properties
                exfat.PartitionOffset = Num.ArrayToULong(buffer, 64, 8);
                exfat.VolumeLength = Num.ArrayToULong(buffer, 72, 8);
                exfat.FatOffset = Num.ArrayToUInt(buffer, 80, 4);
                exfat.FATsize = Num.ArrayToUInt(buffer, 84, 4);
                exfat.ClusterHeapOffset = Num.ArrayToUInt(buffer, 88, 4);
                exfat.ClusterCount = Num.ArrayToUInt(buffer, 92, 4);
                exfat.RootDirClusterNum = Num.ArrayToUInt(buffer, 96, 4);
                exfat.VolumeSerialNum = Num.ArrayToUInt(buffer, 100, 4);
                exfat.FsRevision = Num.ArrayToUInt(buffer, 104, 4);
                exfat.VolumeFlags = Num.ArrayToUInt(buffer, 102, 2);
                exfat.BytesPerSectorShift = buffer[108];
                exfat.SectorPerClusterShift = buffer[109];
                exfat.NumOfFat = buffer[110];
                exfat.DriveSelect = buffer[111];
                exfat.PercentInUse = buffer[112];
                exfat.Reserved = new byte[7];
                Array.Copy(buffer, 113, exfat.Reserved, 0, exfat.Reserved.Length);
                bscmn.BootCode = new byte[390];
                Array.Copy(buffer, 120, bscmn.BootCode, 0, bscmn.BootCode.Length);

                //pre-calculate common properties
                bscmn.FatSizeInSector = (int)exfat.FATsize;
                bscmn.BytesPerSector = 1 << exfat.BytesPerSectorShift;
                bscmn.SectorPerCluster = 1 << exfat.SectorPerClusterShift;
                bscmn.ClusterSizeInBytes = bscmn.SectorPerCluster * bscmn.BytesPerSector;
                bscmn.FatSizeInBytes = bscmn.FatSizeInSector * bscmn.BytesPerSector;
                bscmn.TotalSectors = bscmn.MbrPartitionTable[0].TotalSectors;
                bscmn.PartitionSize = exfat.ClusterCount * bscmn.ClusterSizeInBytes;
                bscmn.FatLocation = (bscmn.MbrPartitionTable[0].StartSector + exfat.FatOffset) * bscmn.BytesPerSector;
                bscmn.DataClusterLocation = (bscmn.MbrPartitionTable[0].StartSector + exfat.ClusterHeapOffset) * bscmn.BytesPerSector;
                bscmn.RootDirLocation = bscmn.DataClusterLocation + GetClusterOffset(exfat.RootDirClusterNum, bscmn.ClusterSizeInBytes);
            }
            else if (bscmn.FsType == FsType.NTFS)
            {
                var ntfsbs = new BootSectorNTFS();
                bscmn.NtfsBS = new List<BootSectorNTFS>();
                bscmn.NtfsBS.Add(ntfsbs);

                //parse NTFS properties
                ntfsbs.TotalSectors = Num.ArrayToULong(buffer, 40, 8);
                ntfsbs.MftMirrFileClusterNum = Num.ArrayToLong(buffer, 48, 8);
                ntfsbs.MftFileClusterNum = Num.ArrayToLong(buffer, 56, 8);
                ntfsbs.ClustersPerFileRecordSegment = Num.ArrayToInt(buffer, 64, 4);
                ntfsbs.ClusterPerIndexBuffer = buffer[68];
                ntfsbs.VolumeSerial = new byte[8];
                Array.Copy(buffer, 72, ntfsbs.VolumeSerial, 0, ntfsbs.VolumeSerial.Length);
                ntfsbs.Checksum = Num.ArrayToInt(buffer, 80, 4);
                bscmn.BootCode = new byte[426];
                Array.Copy(buffer, 84, bscmn.BootCode, 0, bscmn.BootCode.Length);

                //pre-calculate common properties
                bscmn.ClusterSizeInBytes = bscmn.SectorPerCluster * bscmn.BytesPerSector;
                bscmn.TotalSectors = (long)ntfsbs.TotalSectors;
                bscmn.PartitionSize = bscmn.TotalSectors * bscmn.BytesPerSector;
            }
            else // FAT12/16/32
            {
                var fatbs = new BootSectorFAT();
                bscmn.FatBS = new List<BootSectorFAT>();
                bscmn.FatBS.Add(fatbs);

                //pre-calculate common properties
                bscmn.ClusterSizeInBytes = bscmn.BytesPerSector * bscmn.SectorPerCluster;
                bscmn.PartitionSize = bscmn.TotalSectors * bscmn.BytesPerSector;
                bscmn.FatSizeInBytes = bscmn.FatSizeInSector * bscmn.BytesPerSector;
                bscmn.FatLocation = (bscmn.MbrPartitionTable[0].StartSector + bscmn.ResvSectorCount) * bscmn.BytesPerSector;
                bscmn.RootDirLocation = bscmn.FatLocation + (bscmn.FatSizeInSector * bscmn.NumOfFat * bscmn.BytesPerSector);
                bscmn.DataClusterLocation = bscmn.RootDirLocation + (bscmn.RootEntryCount * FatEntry.Length);

                var fat32offset = 0;
                if (bscmn.FsType == FsType.FAT32)
                {
                    //parse FAT32 properties
                    fatbs.FATsize32 = Num.ArrayToLong(buffer, 36, 4);
                    fatbs.ExtFlags = Num.ArrayToInt(buffer, 40, 2);
                    fatbs.FSver = Num.ArrayToInt(buffer, 42, 2);
                    fatbs.RootDirClusterNum = Num.ArrayToUInt(buffer, 44, 4);
                    fatbs.FSInfoSector = Num.ArrayToInt(buffer, 48, 2);
                    fatbs.BackupBootSect = Num.ArrayToInt(buffer, 50, 2);
                    fatbs.ReservedFAT32 = new byte[12];
                    Array.Copy(buffer, 52, fatbs.ReservedFAT32, 0, fatbs.ReservedFAT32.Length);
                    bscmn.BootCode = new byte[420];
                    Array.Copy(buffer, 90, bscmn.BootCode, 0, bscmn.BootCode.Length);
                    fat32offset = 28;

                    //pre-calculate FAT32 properties
                    bscmn.BytesPerFatConst = FatConst.ClusterByteCountFAT32;
                    if (bscmn.FatSizeInBytes <= 0)
                    {
                        bscmn.FatSizeInBytes = fatbs.FATsize32 * bscmn.BytesPerSector;
                        bscmn.DataClusterLocation = bscmn.FatLocation + (fatbs.FATsize32 * bscmn.NumOfFat * bscmn.BytesPerSector);
                        bscmn.RootDirLocation = bscmn.DataClusterLocation + GetClusterOffset(fatbs.RootDirClusterNum, bscmn.ClusterSizeInBytes);
                    }
                }

                //common properties FAT12/16, FAT32 with offset
                fatbs.DriveNumber = Num.ArrayToInt(buffer, fat32offset + 36, 1);
                fatbs.Reserved1 = Num.ArrayToInt(buffer, fat32offset + 37, 1);
                fatbs.BootSignature = Num.ArrayToInt(buffer, fat32offset + 38, 1);
                fatbs.VolumeId = Num.ArrayToHexString(buffer, fat32offset + 39, 4);
                fatbs.VolumeLabel = Encoding.ASCII.GetString(buffer, fat32offset + 43, 11);
                fatbs.FileSysString = Encoding.ASCII.GetString(buffer, fat32offset + 54, 8);

                //concat volume name
                var idx = fatbs.VolumeLabel.IndexOf('\0');
                if (idx >= 0) fatbs.VolumeLabel = fatbs.VolumeLabel.Substring(0, idx);

                //concat FS name
                idx = fatbs.FileSysString.IndexOf('\0');
                if (idx >= 0) fatbs.FileSysString = fatbs.FileSysString.Substring(0, idx);

                //try detect FS from boot sector
                if (bscmn.FsType == FsType.Unknown)
                {
                    if (fatbs.FileSysString.Contains("FAT12")) bscmn.FsType = FsType.FAT12;
                    else if (fatbs.FileSysString.Contains("FAT16")) bscmn.FsType = FsType.FAT16;
                }
            }

            //pre-load FS dependent const
            if (bscmn.FsType == FsType.FAT12)
            {
                bscmn.BadClusterFlagConst = ClusterNum.BadFAT12;
                bscmn.EofClusterFlagConst = ClusterNum.EofFAT12;
            }
            else if (bscmn.FsType == FsType.FAT16)
            {
                bscmn.BytesPerFatConst = FatConst.ClusterByteCountFAT16;
                bscmn.BadClusterFlagConst = ClusterNum.BadFAT16;
                bscmn.EofClusterFlagConst = ClusterNum.EofFAT16;
            }
            else if (bscmn.FsType == FsType.FAT32)
            {
                bscmn.BytesPerFatConst = FatConst.ClusterByteCountFAT32;
                bscmn.BadClusterFlagConst = ClusterNum.BadFAT32;
                bscmn.EofClusterFlagConst = ClusterNum.EofFAT32;
            }
            else if (bscmn.FsType == FsType.ExFAT)
            {
                bscmn.BytesPerFatConst = FatConst.ClusterByteCountFAT32;
                bscmn.BadClusterFlagConst = ClusterNum.BadexFAT;
                bscmn.EofClusterFlagConst = ClusterNum.EofExFAT;
            }
            else if (bscmn.FsType == FsType.NTFS)
            {
                bscmn.BytesPerFatConst = FatConst.ClusterByteCountFAT32;
                bscmn.BadClusterFlagConst = ClusterNum.BadexFAT;
                bscmn.EofClusterFlagConst = ClusterNum.EofExFAT;
            }

            return bscmn;
        }

        public static string GetFormatedSizeString(double size)
        {
            const long kbytes = 1024;
            const long mbytes = kbytes * 1024;
            const long gibytes = mbytes * 1024;
            string sizestr;
            if (size < kbytes) sizestr = string.Format("{0} bytes", size);
            else if (size < mbytes) sizestr = string.Format("{0:0.00} KB", size / kbytes);
            else if (size < gibytes) sizestr = string.Format("{0:0.00} MB", size / mbytes);
            else sizestr = string.Format("{0:0.00} GB", size / gibytes);
            return sizestr;
        }

        #endregion

        #region Default Interface Implementation

        public virtual IEnumerable<FatEntry> GetDirEntries(long startcluster = ROOT_DIR_FAT_12_16)
        {
            throw new NotImplementedException();
        }

        public virtual void ReadFile(FatEntry file, Stream dest)
        {
            throw new NotImplementedException();
        }

        public virtual void WriteFile(FatEntry file, Stream source)
        {
            throw new NotImplementedException();
        }

        public virtual FatEntry CreateFile(FatEntry directory)
        {
            throw new NotImplementedException();
        }

        public virtual void DeleteFile(FatEntry file)
        {
            throw new NotImplementedException();
        }

        public virtual void Format(int sectorsize = DEFAULT_SECT_SIZE)
        {
            throw new NotImplementedException();
        }

        #endregion

    }
}
