using System;
using System.Collections.Generic;

namespace PointCloudConverter.Structs
{
    [Serializable]
    public class LasHeader
    {
        public string FileName { get; set; }
        // v1.2
        public ushort ProjectionID { get; set; } // these are duplicate data from the VLR (just for convenience)
        public string Projection { get; set; }
        public string WKT { get; set; }

        public ushort FileSourceID { get; set; }
        public ushort GlobalEncoding { get; set; }
        public uint ProjectID_GUID_data1 { get; set; }
        public ushort ProjectID_GUID_data2 { get; set; }
        public ushort ProjectID_GUID_data3 { get; set; }
        public byte[] ProjectID_GUID_data4 { get; set; } = new byte[8];
        public byte VersionMajor { get; set; }
        public byte VersionMinor { get; set; }
        public string SystemIdentifier { get; set; } = new string(new char[32]);
        public string GeneratingSoftware { get; set; } = new string(new char[32]);
        public ushort FileCreationDayOfYear { get; set; }
        public ushort FileCreationYear { get; set; }
        public ushort HeaderSize { get; set; }
        public uint OffsetToPointData { get; set; }
        public uint NumberOfVariableLengthRecords { get; set; }
        public byte PointDataFormatID { get; set; }
        public ushort PointDataRecordLength { get; set; }
        public uint NumberOfPointRecords { get; set; }
        public uint[] NumberOfPointsByReturn { get; set; } = new uint[5];
        public double XScaleFactor { get; set; }
        public double YScaleFactor { get; set; }
        public double ZScaleFactor { get; set; }
        public double XOffset { get; set; }
        public double YOffset { get; set; }
        public double ZOffset { get; set; }
        public double MaxX { get; set; }
        public double MinX { get; set; }
        public double MaxY { get; set; }
        public double MinY { get; set; }
        public double MaxZ { get; set; }
        public double MinZ { get; set; }
        public List<LasVariableLengthRecord> VariableLengthRecords { get; set; }
    }
}
