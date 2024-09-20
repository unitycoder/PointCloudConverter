using PointCloudConverterForDotnetCLI.Structs.VariableLengthRecords;
using System;
using System.Collections.Generic;

namespace PointCloudConverterForDotnetCLI.Structs
{
    [Serializable]
    public class LasVariableLengthRecord
    {
        public ushort Reserved { get; set; }
        public string UserID { get; set; } = new string(new char[16]);
        public ushort RecordID { get; set; }
        public ushort RecordLengthAfterHeader { get; set; }
        public string Description { get; set; } = new string(new char[32]);
        public List<sGeoKeys> GeoKeys { get; set; } // GeoKeyDirectoryTag
        public string GeoAsciiParamsTag { get; set; }
        
        // NOT supported:
        // GeoDoubleParamsTag
        // Classification lookup
        // Histogram
        // Text area description
    }
}
