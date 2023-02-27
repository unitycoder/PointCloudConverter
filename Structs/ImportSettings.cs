// these values get filled from commandline arguments

using PointCloudConverter.Readers;
using PointCloudConverter.Structs;
using PointCloudConverter.Writers;
using System.Collections.Generic;

namespace PointCloudConverter
{
    public class ImportSettings
    {
        public IReader reader = new LAZ();
        public IWriter writer = new UCPC();

        public bool haveError = false; // if errors during parsing args
        //public string[] errorMessages = null; // last error message(s)

        public bool useScale = false;
        public float scale = 1f;

        public ImportFormat importFormat = ImportFormat.LAS; //default to las for now
        public ExportFormat exportFormat = ExportFormat.UCPC; // defaults to UCPC (v2)

        public List<string> inputFiles = new List<string>();
        public string outputFile = null;

        public List<string> errors = new List<string>(); // return errors to UI

        // FIXME default values will be used unless otherwise specified.. randomize = true 
        // TODO these should be export settings..

        public bool swapYZ = true;
        public bool invertX = false;
        public bool invertZ = false;
        public bool importRGB = true; // this or intensity must be on
        public bool importIntensity = false;
        public bool useAutoOffset = true;
        public float offsetX = 0;
        public float offsetY = 0;
        public float offsetZ = 0;
        public bool useLimit = false;
        public int limit = 0;
        public bool randomize = false;
        public float gridSize = 25;
        public int minimumPointCount = 0;
        public bool packColors = false;
        public int packMagicValue = 64; // use lower value if your gridsize is very large, if gridsize=500 then try value 2
        public bool skipPoints = false;
        public int skipEveryN = 0;
        public bool keepPoints = false; // TODO rename to useKeepPoints?
        public int keepEveryN = 0;
        public int maxFiles = 0;
        public bool batch = false;
        public bool useManualOffset = false;
        public float manualOffsetX = 0;
        public float manualOffsetY = 0;
        public float manualOffsetZ = 0;

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
            return t;
        }
    }
}
