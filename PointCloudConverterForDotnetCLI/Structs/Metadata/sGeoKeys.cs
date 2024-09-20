using System;
using System.Collections.Generic;

// GeoKeyDirectoryTag

namespace PointCloudConverterForDotnetCLI.Structs.VariableLengthRecords
{
    [Serializable]
    public class sGeoKeys
    {
        public ushort KeyDirectoryVersion { get; set; }
        public ushort KeyRevision { get; set; }
        public ushort MinorRevision { get; set; }
        public ushort NumberOfKeys { get; set; }
        public List<sKeyEntry> KeyEntries { get; set; } = new List<sKeyEntry>();
    }

    [Serializable]
    public class sKeyEntry
    {
        public ushort KeyID { get; set; }
        public string KeyIDString { get; set; }
        public ushort TIFFTagLocation { get; set; }
        public ushort Count { get; set; }
        public ushort Value_Offset { get; set; }
        public string Value_OffsetString { get; set; }
    }
}
