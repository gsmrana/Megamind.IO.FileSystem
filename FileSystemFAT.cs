using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public class FileSystemFAT : FileSystemBase
    {
        #region ctor

        public FileSystemFAT(IStorageIO storageIO)
            : base(storageIO)
        {
        }

        #endregion

        #region Public Methods

        string _lfnstring = "";

        public override IEnumerable<FatEntry> GetDirEntries(long startcluster = ROOT_DIR_FAT_12_16)
        {
            var bs = BootSector;
            UpdateProgress(0);

            var readingRootDir = false;
            var clusternum = startcluster;
            if (startcluster == ROOT_DIR_FAT_12_16)
            {
                readingRootDir = true;
                clusternum = ClusterNum.PreserveCount;
            }

            EventLog(string.Format("\r\nReading FAT entry => Partition: {0}, StartCluster: {1}0x{2:X8}, FatLocation: 0x{3:X8}, FatSizeInBytes: {4} bytes",
                     bs.PartitionId, readingRootDir ? "ROOT_DIR, " : "", startcluster, bs.FatLocation, bs.FatSizeInBytes));
            var fatbytes = new byte[bs.FatSizeInBytes];
            _storageIO.Seek(bs.FatLocation);
            _storageIO.ReadBytes(fatbytes, fatbytes.Length);
            UpdateProgress(40);

            var progress = 0;
            var entryCount = 0;
            var clusterLoopCount = 0;
            var fatentry = new List<FatEntry>();
            while (clusternum < bs.EofClusterFlagConst)
            {
                long entrylocation;
                if (readingRootDir) entrylocation = bs.RootDirLocation + GetClusterOffset(clusternum, bs.ClusterSizeInBytes);
                else entrylocation = bs.DataClusterLocation + GetClusterOffset(clusternum, bs.ClusterSizeInBytes);

                var entrydata = new byte[bs.ClusterSizeInBytes];
                _storageIO.Seek(entrylocation);
                _storageIO.ReadBytes(entrydata, entrydata.Length);
                EventLog(string.Format("\r\nParsing entry => ClusterNum: {0}0x{1:X8}, Location: 0x{2:X8}", readingRootDir ? "ROOT_DIR, " : "", clusternum, entrylocation));
                UpdateProgress(40 + (progress += 10) % 60);

                var reachedToNullEntry = false;
                for (int offset = 0; offset < bs.ClusterSizeInBytes; offset += FatEntry.Length)
                {
                    entryCount++;
                    var entry = ParseFatEntry(entrydata, offset);
                    var attribute = ((EntryAttributes)entry.Attribute).ToString();
                    var tabs = (attribute.Length >= 11) ? "\t" : "\t\t";
                    if (attribute.Length <= 2) tabs = "\t\t\t";
                    EventLog(string.Format("{0:000} => Cluster: 0x{1:X8}, Size: 0x{2:X8}, Attribute: 0x{3:X2} -> {4},{5}Entry[0]: 0x{6:X2} -> {7}",
                            entryCount, entry.StartCluster, entry.FileSize, entry.Attribute, attribute, tabs, entry.EntryType, (FatEntryType)entry.EntryType));

                    // EntryByte0 validity check
                    if (entry.EntryType == (byte)FatEntryType.Null)
                    {
                        //break the fat entry parsing loop
                        reachedToNullEntry = true;
                        break;
                    }
                    if (entry.EntryType == (byte)FatEntryType.DeletedEntry && !IncludeDeletedEntry) continue;

                    if (entry.Attribute == (byte)EntryAttributes.LongFileName)
                    {
                        var lfnentry = ParseLfnEntry(entrydata, offset);
                        _lfnstring = lfnentry.Name + _lfnstring;
                    }
                    else
                    {
                        if (_lfnstring != "")
                        {
                            entry.LongName = _lfnstring;
                            entry.LfnAvailable = true;
                            _lfnstring = "";
                        }
                        entry.EntryIndex = entryCount;
                        entry.Partition = this;
                        fatentry.Add(entry);
                    }
                }

                // break cluster chain loop
                if (reachedToNullEntry)
                {
                    EventLog("Reached to null fat entry!");
                    break;
                }

                // in case of getting cluster chain loop trap, 3200 entry max
                if (clusterLoopCount++ > 200)
                {
                    EventLog("Error: Too much loop in cluster chain, breaking forcibly!");
                    break;
                }

                //get next cluster number
                long nextcluster;
                if (readingRootDir && bs.RootEntryCount > 0) nextcluster = clusternum + 1;
                else if (bs.FsType == FsType.FAT12) nextcluster = GetFAT12NextCluster(fatbytes, (int)clusternum);
                else nextcluster = Num.ArrayToLong(fatbytes, clusternum * bs.BytesPerFatConst, bs.BytesPerFatConst);
                EventLog(string.Format("NextCluster =>  0x{0:X8}", nextcluster));

                if (nextcluster <= ClusterNum.PreserveCount)
                {
                    EventLog("Error: Invalid cluster mark specified in chain!");
                    break;
                }
                else if (nextcluster == bs.BadClusterFlagConst)
                {
                    EventLog("Error: Bad cluster mark found!");
                }
                else if (nextcluster >= bs.EofClusterFlagConst)
                {
                    EventLog("Reached to Eof cluster mark!");
                }

                clusternum = nextcluster;
            }

            UpdateProgress(100);
            EventLog("\r\nFile/Directory Found: " + fatentry.Count);
            return fatentry;
        }

        public override void ReadFile(FatEntry file, Stream dest)
        {
            var bs = file.Partition.BootSector;
            var clusternum = file.StartCluster;
            var bytes2read = file.FileSize;
            long fatlength = bs.FatSizeInBytes;
            var datacluster = bs.DataClusterLocation;
            UpdateProgress(0);

            EventLog(string.Format("ReadFile => ClusterNum: 0x{0:X8}, Location: 0x{1:X8}, Size: 0x{2:X8}, FATlocation: 0x{3:X8}, FATlength: {4} bytes",
                        clusternum, datacluster + GetClusterOffset(clusternum, bs.ClusterSizeInBytes), bytes2read, bs.FatLocation, fatlength));
            var fatbytes = new byte[fatlength];
            _storageIO.Seek(bs.FatLocation);
            _storageIO.ReadBytes(fatbytes, fatbytes.Length);
            UpdateProgress(10);

            while (bytes2read > 0)
            {
                if (clusternum < ClusterNum.PreserveCount)
                {
                    EventLog("Error: Invalid cluster mark specified in chain!");
                    break;
                }
                else if (clusternum == bs.BadClusterFlagConst)
                {
                    EventLog("Error: Bad cluster mark found!");
                }
                else if (clusternum >= bs.EofClusterFlagConst)
                {
                    EventLog("Error: Unexpected end of cluster chain!");
                    break;
                }

                //read file data
                var databuff = new byte[bs.ClusterSizeInBytes];
                _storageIO.Seek(datacluster + GetClusterOffset(clusternum, bs.ClusterSizeInBytes));
                _storageIO.ReadBytes(databuff, databuff.Length);

                //write file data
                var block2write = bs.ClusterSizeInBytes;
                if (bytes2read < bs.ClusterSizeInBytes) block2write = bytes2read;
                dest.Write(databuff, 0, block2write);

                //update progress
                bytes2read -= block2write;
                var progress = (long)(file.FileSize - bytes2read) * 100 / file.FileSize;
                UpdateProgress((int)progress);

                //get next cluster number
                if (bs.FsType == FsType.FAT12) clusternum = GetFAT12NextCluster(fatbytes, clusternum);   //FAT12
                else clusternum = Num.ArrayToInt(fatbytes, clusternum * bs.BytesPerFatConst, bs.BytesPerFatConst); //FAT16 and FAT32
                EventLog(string.Format("ReadFile => NextCluster : 0x{0:X8}, Remaining: {1} bytes", clusternum, bytes2read));
            }

            UpdateProgress(100);
            EventLog(string.Format("ReadFile Completed: {0} bytes", file.FileSize - bytes2read));
        }

        public IEnumerable<int> FatDump(int partition)
        {
            var bs = BootSector;
            long fatlength = bs.FatSizeInBytes;
            double bytesperfat = 2;
            var fatnumcount = fatlength / (int)bytesperfat;

            if (bs.FsType == FsType.FAT12)
            {
                bytesperfat = 1.5;
                fatnumcount = (int)(fatlength / bytesperfat) - 1;
            }
            else if (bs.FsType == FsType.FAT32 || bs.FsType == FsType.ExFAT || bs.FsType == FsType.NTFS)
            {
                bytesperfat = 4;
                fatnumcount = fatlength / (int)bytesperfat;
            }

            EventLog(string.Format("\r\nDumping FAT => FATlocation: 0x{0:X8}, BytesPerFAT: {1}, FATlength: {2} bytes", bs.FatLocation, bytesperfat, fatlength));
            var fatbytes = new byte[fatlength];
            _storageIO.Seek(bs.FatLocation);
            _storageIO.ReadBytes(fatbytes, fatbytes.Length);

            var fatdump = new List<int>();
            for (int i = 0; i < fatnumcount; i++)
            {
                int clsnum;
                if (bytesperfat == 1.5) clsnum = GetFAT12NextCluster(fatbytes, i);
                else clsnum = Num.ArrayToInt(fatbytes, i * (int)bytesperfat, (int)bytesperfat);
                fatdump.Add(clsnum);
            }
            return fatdump;
        }

        #endregion

        #region Private  Methods

        private static FatEntry ParseFatEntry(byte[] buffer, int offset)
        {
            var entry = new FatEntry
            {
                ShortName = Encoding.ASCII.GetString(buffer, offset + 0, 8),
                Ext = Encoding.ASCII.GetString(buffer, offset + 8, 3),
                Attribute = Num.ArrayToInt(buffer, offset + 11, 1),
                NTReserved = Num.ArrayToInt(buffer, offset + 12, 1),
                CreateTimeTenth = Num.ArrayToInt(buffer, offset + 13, 1),
                CreateTime = Num.ArrayToInt(buffer, offset + 14, 2),
                CreateDate = Num.ArrayToInt(buffer, offset + 16, 2),
                LastAccessDate = Num.ArrayToInt(buffer, offset + 18, 2),
                StartClusterHI = Num.ArrayToInt(buffer, offset + 20, 2),
                WritetTime = Num.ArrayToInt(buffer, offset + 22, 2),
                WriteDate = Num.ArrayToInt(buffer, offset + 24, 2),
                StartClusterLO = Num.ArrayToInt(buffer, offset + 26, 2),
                FileSize = Num.ArrayToInt(buffer, offset + 28, 4),
            };
            entry.EntryType = buffer[offset + 0];
            entry.StartCluster = (entry.StartClusterHI << 16) + entry.StartClusterLO;
            //copy raw data bytes
            entry.RawBytes = new byte[FatEntry.Length];
            Array.Copy(buffer, offset, entry.RawBytes, 0, entry.RawBytes.Length);
            return entry;
        }

        private static FatLfnEntry ParseLfnEntry(byte[] buffer, int offset)
        {
            var lfn = new FatLfnEntry
            {
                Order = Num.ArrayToInt(buffer, offset + 0, 1),
                Name1 = Encoding.Unicode.GetString(buffer, offset + 1, 10),
                Attribute = Num.ArrayToInt(buffer, offset + 11, 1),
                Type = Num.ArrayToInt(buffer, offset + 12, 1),
                Checksum = Num.ArrayToInt(buffer, offset + 13, 1),
                Name2 = Encoding.Unicode.GetString(buffer, offset + 14, 12),
                FirstClusterLO = Num.ArrayToInt(buffer, offset + 26, 2),
                Name3 = Encoding.Unicode.GetString(buffer, offset + 28, 4)
            };
            return lfn;
        }

        #endregion
    }
}
