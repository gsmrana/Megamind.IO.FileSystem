using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Megamind.IO.FileSystem
{
    public class FileManager
    {
        #region Data

        protected IStorageIO _storageIO;
        public MasterBootRecord Mbr { get; set; }
        public List<BootSectorCommon> BootSectors { get; set; }
        public List<FileSystemBase> Partitions { get; set; }

        #endregion

        #region ctor

        public FileManager(IStorageIO storageIO)
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

        #region Public Methods

        public virtual void Open()
        {
            //try to read MBR partition
            UpdateProgress(0);
            var mbrdata = new byte[MasterBootRecord.Length];
            _storageIO.Seek(0);
            _storageIO.ReadBytes(mbrdata, mbrdata.Length);
            Mbr = FileSystemBase.ParseMBR(mbrdata);
            UpdateProgress(10);

            //no MBR, add default partition table
            if (Mbr.PartitionTable.Count == 0)
            {
                Mbr = new MasterBootRecord();
                Mbr.PartitionTable = new List<MbrPartitionTable>();
                Mbr.PartitionTable.Add(new MbrPartitionTable { StartSector = 0 });
            }

            EventLog("\r\nMBR Found: " + (Mbr.IsSignatureValid ? "Yes" : "No"));
            var count = 0;
            if (Mbr.IsSignatureValid)
            {
                EventLog("MBR Partition Count: " + Mbr.PartitionTable.Count);
                foreach (var item in Mbr.PartitionTable)
                {
                    count++;
                    EventLog(string.Format("{0}. MBR Partition => StartSector: 0x{1:X8}, TotalSector: 0x{2:X8}, FileSystemMbr: 0x{3:X2} -> {4}",
                        count, item.StartSector, item.TotalSectors, item.FileSystemMbr, (FsTypesMbr)item.FileSystemMbr));
                }
            }

            //read boot sectors
            BootSectors = new List<BootSectorCommon>();
            Partitions = new List<FileSystemBase>();
            foreach (var pt in Mbr.PartitionTable)
            {
                if(pt.FileSystemMbr == (int)FsTypesMbr.GptParitionSignature)
                {
                    EventLog("Error: GPT Partition Table not supported yet!");
                    break;
                }
                
                var bsdata = new byte[BootSectorCommon.Length];
                _storageIO.Seek(pt.StartSector * FatConst.BytesPerSectorDefault);
                _storageIO.ReadBytes(bsdata, bsdata.Length);
                var bs = FileSystemBase.ParseBootSector(pt, bsdata);
                bs.PartitionId = BootSectors.Count;
                BootSectors.Add(bs);

                var fs = new FileSystemBase(_storageIO);
                if (bs.FsType == FsType.NTFS) fs = new FileSystemNTFS(_storageIO);
                else if (bs.FsType == FsType.ExFAT) fs = new FileSystemExFAT(_storageIO);
                else if (bs.FsType == FsType.FAT12 || bs.FsType == FsType.FAT16 || bs.FsType == FsType.FAT32)
                    fs = new FileSystemFAT(_storageIO);
                fs.OnEventLog += OnEventLog;
                fs.OnProgress += OnProgress;
                fs.BootSector = bs;
                Partitions.Add(fs);
            }
            UpdateProgress(40);

            count = 0;
            EventLog("\r\nBootSector Partition Count: " + BootSectors.Count);
            foreach (var bs in BootSectors)
            {
                count++;
                EventLog(string.Format("{0}. BS  Partition => StartSector: 0x{1:X8}, TotalSector: 0x{2:X8}, MediaType: 0x{3:X2}, FileSystem: {4}",
                   count, bs.MbrPartitionTable.First().StartSector, bs.TotalSectors, bs.MediaType, bs.FsType));
            }
            UpdateProgress(100);
        }

        public virtual void Close()
        {
            _storageIO.Close();
        }

        #endregion

    }
}
