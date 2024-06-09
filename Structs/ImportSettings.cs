// these values get filled from commandline arguments

using PointCloudConverter.Readers;
using PointCloudConverter.Structs;
using PointCloudConverter.Writers;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PointCloudConverter
{
    public class ImportSettings
    {
        // filled in by program (so that json serializer is easier)
        public string version { get; set; } = "0.0.0";
        
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Logger.LogEvent @event { get; set; }

        public IReader reader = new LAZ();
        public IWriter writer = new UCPC();

        public bool haveError { get; set; } = false; // if errors during parsing args
        //public string[] errorMessages = null; // last error message(s)

        public bool useScale { get; set; } = false;
        public float scale { get; set; } = 1f;

        [JsonConverter(typeof(JsonStringEnumConverter))] 
        public ImportFormat importFormat { get; set; } = ImportFormat.LAS; //default to las for now
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExportFormat exportFormat { get; set; } = ExportFormat.UCPC; // defaults to UCPC (v2)

        public List<string> inputFiles { get; set; } = new List<string>();
        public string outputFile { get; set; } = null;

        public List<string> errors { get; set; } = new List<string>(); // return errors to UI

        // FIXME default values will be used unless otherwise specified.. randomize = true 
        // TODO these should be export settings..

        public bool importRGB { get; set; } = true; // this or intensity must be on
        public bool importIntensity { get; set; } = false;
        public bool useAutoOffset { get; set; } = true;
        public bool swapYZ { get; set; } = true;
        public bool invertX { get; set; } = false;
        public bool invertZ { get; set; } = false;
        public float offsetX { get; set; } = 0;
        public float offsetY { get; set; } = 0;
        public float offsetZ { get; set; } = 0;
        public bool useLimit { get; set; }  = false;
        public int limit { get; set; } = 0;
        public bool randomize { get; set; } = false;
        public float gridSize { get; set; } = 25;
        public int minimumPointCount { get; set; } = 0;
        public bool packColors { get; set; } = false;
        public int packMagicValue { get; set; } = 64; // use lower value if your gridsize is very large, if gridsize=500 then try value 2
        public bool skipPoints { get; set; } = false;
        public int skipEveryN { get; set; } = 0;
        public bool keepPoints { get; set; } = false; // TODO rename to useKeepPoints?
        public int keepEveryN { get; set; } = 0;
        public int maxFiles { get; set; } = 0;
        public bool batch { get; set; } = false;
        public bool useManualOffset { get; set; } = false;
        public float manualOffsetX { get; set; } = 0;
        public float manualOffsetY { get; set; } = 0;
        public float manualOffsetZ { get; set; } = 0;
        public bool useCustomIntensityRange { get; set; } = false; // if false, 0-255 range is used, if ture: 0-65535
        public int seed { get; set; } = -1; // random seed for shuffling
        public bool useJSONLog = false;
        public bool importMetadata = false;
        public bool importMetadataOnly = false;
        public bool averageTimestamp = false; // calculate average timestamp for all points for this tile

        public override string ToString()
        {
            string t = "";
            t += " useScale=" + useScale;
            t += "\n scale=" + scale;
            t += "\n inputFiles=" + inputFiles;
            t += "\n outputFile=" + outputFile;
            t += "\n swapYZ=" + swapYZ;
            t += "\n invertX=" + invertX;
            t += "\n invertZ=" + invertZ;
            t += "\n readRGB=" + importRGB;
            t += "\n readIntensity=" + importIntensity;
            //t += "\n metaData=" + importIntensity;
            t += "\n useAutoOffset=" + useAutoOffset;
            t += "\n offsetX=" + offsetX;
            t += "\n offsetY=" + offsetY;
            t += "\n offsetZ=" + offsetZ;
            t += "\n useLimit=" + useLimit;
            t += "\n limit=" + limit;
            t += "\n randomize=" + randomize;
            t += "\n gridSize=" + gridSize;
            t += "\n minimumPointCount=" + minimumPointCount;
            t += "\n packColors=" + packColors;
            t += "\n packMagicValue=" + packMagicValue;
            t += "\n skipPoints=" + skipPoints;
            t += "\n skipEveryN=" + skipEveryN;
            t += "\n keepPoints=" + keepPoints;
            t += "\n keepEveryN=" + keepEveryN;
            t += "\n maxFiles=" + maxFiles;
            t += "\n batch=" + batch;
            t += "\n useManualOffset=" + useManualOffset;
            t += "\n manualOffsetX=" + manualOffsetX;
            t += "\n manualOffsetX=" + manualOffsetX;
            t += "\n manualOffsetX=" + manualOffsetX;
            t += "\n useCustomIntensityRange=" + useCustomIntensityRange;
            t += "\n seed=" + seed;
            t += "\n useJSONLog=" + useJSONLog;
            return t;
        }

        internal string ToJSON()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
