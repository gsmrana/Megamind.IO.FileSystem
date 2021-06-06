using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public static class FatConst
    {
        public const int ClusterByteCountFAT16 = 2;
        public const int ClusterByteCountFAT32 = 4;
        public const int BytesPerSectorDefault = 512;
        public static readonly string SectorSignature = "55AA"; //hex
    }

    public static class ClusterNum
    {
        public const int PreserveCount = 2;

        public const uint BadFAT12 = 0x0FF7;
        public const uint BadFAT16 = 0xFFF7;
        public const uint BadFAT32 = 0x0FFFFFF7;
        public const uint BadexFAT = 0xFFFFFFF7;

        public const uint EofFAT12 = 0x0FF8;
        public const uint EofFAT16 = 0xFFF8;
        public const uint EofFAT32 = 0x0FFFFFF8;
        public const uint EofExFAT = 0xFFFFFFF8;

        public const uint MaxFAT12 = 0x0FFF;
        public const uint MaxFAT16 = 0xFFFF;
        public const uint MaxFAT32 = 0x0FFFFFFF;
        public const uint MaxExFAT = 0xFFFFFFFF;
    }

    public enum FsType
    {
        Unknown,
        FAT12,
        FAT16,
        FAT32,
        ExFAT,
        NTFS
    }

    public enum FsTypesMbr : byte
    {
        Null = 0x00,
        FAT12_CHS = 0x01,
        FAT16_CHS = 0x04,
        Extended_CHS = 0x05,
        FAT12_16_CHS = 0x06,
        NTFS_ExFAT_CHS = 0x07,
        FAT32_CHS = 0x0B,
        FAT32_LBA = 0x0C,
        FAT12_16_LBA = 0x0E,
        Extended_LBA = 0x0F,
        GptParitionSignature = 0xEE
    }

    public enum EntryAttributes : byte
    {
        Null = 0x00,
        ReadOnly = 0x01,
        Hidden = 0x02,
        SystemFile = 0x04,
        VolumeLabel = 0x08,
        LongFileName = 0x0F,
        Directory = 0x10,
        HiddenSystemDir = 0x16,
        Archive = 0x20,
        Device = 0x40,
    }

    // 1st byte of dir entry
    public enum FatEntryType : byte
    {
        Null = 0x00,            //Not allocated
        NameStartWithE5 = 0x05, //heading name character 0xE5, 0x05 is set instead
        DotDirEntry = 0x2E,     //this(.) and up(..) dir entry
        DeletedEntry = 0xE5,    //FAT12/16/32 deleted flag

        //exFAT specific
        AllocationBMP = 0x81,
        UpCaseTable = 0x82,
        ExFatVolumeLabel = 0x83,
        ExFatFileDirectory = 0x85,
        StreamExt = 0xC0,
        ExFatFileName = 0xC1,
        WinCeACL = 0xC2,
        VolumeGuid = 0xA0,
        TextFatPadding = 0xA1,
        WinCeAclTable = 0xA2
    }

    public class MasterBootRecord
    {
        public byte[] BootCode { get; set; }
        public string DriveSerialNo { get; set; } = String.Empty;
        public string CopyProtectionFlag { get; set; } = String.Empty;
        public bool IsCopyProtected { get => CopyProtectionFlag.Equals(CopyProtectedFlag); }
        public List<MbrPartitionTable> PartitionTable { get; set; }
        public string Signature { get; set; } = String.Empty;
        public bool IsSignatureValid { get => Signature.Equals(FatConst.SectorSignature); }
        public byte[] RawBytes { get; set; }

        public static readonly string CopyProtectedFlag = "5A5A"; //hex
        public const int BootCodeSize = 440;
        public const int PartitionTableIndex = 446;
        public const int MaxPartition = 4;
        public const int Length = 512;
    }

    public class MbrPartitionTable
    {
        public bool Bootable { get => BootFlag.Equals(BootableFlag); }
        public string BootFlag { get; set; } = String.Empty;
        public string ChsBegin { get; set; } = String.Empty;
        public string ChsEnd { get; set; } = String.Empty;
        public int FileSystemMbr { get; set; }
        public string FileSystemString { get => ((FsTypesMbr)FileSystemMbr).ToString(); }
        public int StartSector { get; set; }
        public int TotalSectors { get; set; }

        public static readonly string BootableFlag = "80"; //hex
        public const int Length = 16;
    }

    public class BootSectorCommon
    {

        // list taken for grid view only
        public List<MbrPartitionTable> MbrPartitionTable { get; set; } 

        public string JumpCode { get; set; } = String.Empty;
        public string OemName { get; set; } = String.Empty;
        public int BytesPerSector { get; set; }
        public int SectorPerCluster { get; set; }
        public int ResvSectorCount { get; set; }
        public int NumOfFat { get; set; }
        public int RootEntryCount { get; set; }
        public int TotalSector16 { get; set; }
        public int MediaType { get; set; }
        public int FatSizeInSector { get; set; }
        public int SectorPerTrack { get; set; }
        public int NumOfHeads { get; set; }
        public long HiddenSectorCount { get; set; }
        public long TotalSector32 { get; set; }

        // appended properties
        public int PartitionId { get; set; }
        public FsType FsType { get; set; }
        public List<BootSectorFAT> FatBS { get; set; }      // list taken for grid view only
        public List<BootSectorExFAT> ExFatBS { get; set; }  // list taken for grid view only
        public List<BootSectorNTFS> NtfsBS { get; set; }    // list taken for grid view only

        //pre-calculated properties
        public int ClusterSizeInBytes { get; set; }
        public long FatSizeInBytes { get; set; }
        public long FatLocation { get; set; }
        public long DataClusterLocation { get; set; }
        public long RootDirLocation { get; set; }
        public long TotalSectors { get; set; }
        public long PartitionSize { get; set; }
        public string PartitionSizeString { get => FileSystemBase.GetFormatedSizeString(PartitionSize); }

        //FS dependent const
        public int BytesPerFatConst { get; set; }
        public uint BadClusterFlagConst { get; set; }
        public uint EofClusterFlagConst { get; set; }

        public string Signature { get; set; } = String.Empty;
        public bool IsSignatureValid { get => Signature.Equals(FatConst.SectorSignature); }
        public byte[] BootCode { get; set; }
        public byte[] RawBytes { get; set; }

        public const int BootCodeLength = 448;
        public const int Length = 512;
    }

    public class BootSectorFAT
    {
        // FAT12, FAT16, FAT32 common
        public int DriveNumber { get; set; }
        public int Reserved1 { get; set; }
        public int BootSignature { get; set; }
        public string VolumeId { get; set; } = String.Empty;
        public string VolumeLabel { get; set; } = String.Empty;
        public string FileSysString { get; set; } = String.Empty;

        // FAT32 specific
        public long FATsize32 { get; set; }
        public int ExtFlags { get; set; }
        public int FSver { get; set; }
        public uint RootDirClusterNum { get; set; }
        public int FSInfoSector { get; set; }
        public int BackupBootSect { get; set; }
        public byte[] ReservedFAT32 { get; set; }
    }

    public class FatEntry
    {
        public FileSystemBase Partition { get; set; }
        public byte EntryType { get; set; } //1st byte of FAT entry
        public string ShortName { get; set; }
        public string Ext { get; set; }

        // derived properties
        public bool LfnAvailable { get; set; }
        public string LongName { get; set; }
        public string FullName
        {
            get
            {
                var fullname = "";
                if (LfnAvailable)
                {
                    fullname = LongName;
                }
                else if (!string.IsNullOrEmpty(ShortName))
                {
                    fullname = ShortName.TrimEnd(' ');
                    if (!string.IsNullOrEmpty(Ext))
                    {
                        var ext = Ext.TrimEnd(' ');
                        if (!string.IsNullOrEmpty(ext))
                        {
                            if (Attribute == (byte)EntryAttributes.Archive)
                                fullname += "." + ext;
                            else fullname += ext;
                        }
                    }
                }
                var idx = fullname.IndexOf('\0');
                if (idx > 0) fullname = fullname.Substring(0, idx);
                return fullname;
            }
        }

        public int Attribute { get; set; }
        public int NTReserved { get; set; }
        public int CreateTimeTenth { get; set; }
        public int CreateTime { get; set; }
        public int CreateDate { get; set; }
        public int LastAccessDate { get; set; }
        public int StartClusterHI { get; set; }
        public int WritetTime { get; set; }
        public int WriteDate { get; set; }
        public int StartClusterLO { get; set; }
        public int FileSize { get; set; }
        public string FileSizeString { get => FileSystemBase.GetFormatedSizeString((uint)FileSize); }

        // appended properties
        public int StartCluster { get; set; }
        public int EntryIndex { get; set; }
        public int EntrySetCount { get; set; }
        public byte[] RawBytes { get; set; }

        public const int Length = 32;
    }

    public class FatLfnEntry
    {
        public int Order { get; set; }
        public string Name1 { get; set; }
        public int Attribute { get; set; }
        public int Type { get; set; }
        public int Checksum { get; set; }
        public string Name2 { get; set; }
        public int FirstClusterLO { get; set; }
        public string Name3 { get; set; }
        public string Name { get { return Name1 + Name2 + Name3; } }

        public const int Length = 32;
    }


}
