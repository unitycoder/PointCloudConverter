// values from commandline arguments

using PointCloudConverter.Logger;
using PointCloudConverter.Plugins;
using PointCloudConverter.Readers;
using PointCloudConverter.Structs;
using PointCloudConverter.Writers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PointCloudConverter
{
    public class ImportSettings
    {
        // filled in by program (so that json serializer is easier), not used
        //public string version { get; set; } = "0.0.0";

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Logger.LogEvent @event { get; set; }
        
        [JsonIgnore] // FIXME doesnt ígnore it
        public IReader reader; // single threaded reader
        //public Dictionary<int?, IReader> Readers { get; set; } = new Dictionary<int?, IReader>();
        public ConcurrentDictionary<int?, IReader> Readers { get; set; } = new ConcurrentDictionary<int?, IReader>();
        [JsonIgnore] 
        public IWriter writer = new UCPC();

        public string ReaderType => reader?.GetType().Name;
        public string WriterType => writer?.GetType().Name;

        //public Dictionary<int?, IWriter> Writers { get; set; } = new Dictionary<int?, IWriter>();
        //public ConcurrentDictionary<int?, WeakReference<IWriter>> Writers { get; set; } = new ConcurrentDictionary<int?, WeakReference<IWriter>>();
        private readonly ConcurrentBag<IWriter> _writerPool = new ConcurrentBag<IWriter>();
        private readonly ConcurrentDictionary<int?, IWriter> _allocatedWriters = new ConcurrentDictionary<int?, IWriter>();
        private int _maxWriters = 16;

        static ILogger Log;


        public void InitWriterPool(int maxThreads, ExportFormat export)
        {
            //exportFormat = export;
            _maxWriters = maxThreads;
            // Initialize the pool with the maximum number of writers
            for (int i = 0; i < _maxWriters; i++)
            {
                _writerPool.Add(CreateNewWriter()); // Create and add writers to the pool
            }
        }

        // Method to get or create a reader for a specific task ID
        public IReader GetOrCreateReader(int? taskId)
        {
            //Log.Write(">>>>> Getting or creating reader for task ID: " + taskId+" format: "+importFormat);

            if (!Readers.ContainsKey(taskId))
            {
                IReader readerInstance;

                switch (importFormat)
                {
                    case ImportFormat.LAS:
                        readerInstance = new LAZ(taskId);
                        break;
                    case ImportFormat.PLY:
                        readerInstance = new PLY(); // no taskId needed here
                        break;
                    case ImportFormat.E57:
                        readerInstance = new E57();
                        break;
                    default:
                        Log.Write($"Unsupported import format: {importFormat}", LogEvent.Error);
                        throw new NotSupportedException($"Unsupported import format: {importFormat}");
                }

                Readers[taskId] = readerInstance;
            }

            //Log.Write(">>>>> Total Readers in dictionary: " + Readers.Count);

            return Readers[taskId];
        }

        private IWriter CreateNewWriter()
        {
            ///Log.Write(">>>>> Creating new writer: "+exportFormat);
            switch (exportFormat)
            {
                case ExportFormat.Unknown:
                    Log.Write("Writer format not specified", LogEvent.Error);
                    return null;
                    break;
                case ExportFormat.UCPC:
                    return new UCPC();
                    break;
                case ExportFormat.PCROOT:
                    return new PCROOT(null); // No taskId when creating the pool, it's assigned later
                    break;
                case ExportFormat.External:
                    // get name from current writer type
                    string dynamicWriterName = writer.GetType().Name.ToUpper();
                    //Trace.WriteLine("Dynamic writer name: " + dynamicWriterName);

                    var dynamicWriter = PluginLoader.LoadWriter(dynamicWriterName);

                    if (dynamicWriter != null)
                    {
                        return dynamicWriter;
                    }
                    else
                    {
                        Log.Write("Dynamic writer not found: " + dynamicWriterName, LogEvent.Error);
                        return null;
                    }

                    return writer; // FIXME this should be loaded from a plugin inside argparser -exportformat code
                    break;
                default:
                    Log.Write("Writer format not supported: " + exportFormat, LogEvent.Error);
                    return null;
                    break;
            }
        }

        public IWriter GetOrCreateWriter(int? taskId)
        {
            if (!_allocatedWriters.TryGetValue(taskId, out var writer))
            {
                // Try to get a writer from the pool
                if (_writerPool.TryTake(out writer))
                {
                    // Assign the writer to the task
                    _allocatedWriters[taskId] = writer;
                }
                else
                {
                    // If no writers are available, create a new one (this should rarely happen if the pool is well-sized)
                    writer = CreateNewWriter();
                    _allocatedWriters[taskId] = writer;
                }
            }

            return writer;
        }

        public void ReleaseWriter(int? taskId)
        {
            if (taskId.HasValue && _allocatedWriters.TryRemove(taskId, out var writer))
            {
                //  Log.Write("ReleaseWriter >>> Memory used: " + GC.GetTotalMemory(false));
                // Clean up the writer if necessary
                writer?.Cleanup(0);
                //writer?.Dispose();
                // Return the writer to the pool for reuse
                _writerPool.Add(writer);
                // Log.Write("ReleaseWriter >>> Memory used: " + GC.GetTotalMemory(false));

            }
        }

        public void ReleaseReader(int? taskId)
        {
            // Log the release of the reader for the specified task ID
            // Log.Write(">>>>> Releasing reader for task ID: " + taskId);

            if (taskId.HasValue)
            {
                if (Readers.TryRemove(taskId, out var reader))
                {
                    reader?.Close();
                    // reader?.Dispose();
                }
                else
                {
                    Log.Write($"Reader for task ID {taskId} could not be removed because it was not found.", LogEvent.Warning);
                }
            }
        }

        public bool haveError { get; set; } = false; // if errors during parsing args
        //public string[] errorMessages = null; // last error message(s)

        public bool useScale { get; set; } = false;
        public float scale { get; set; } = 1f;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ImportFormat importFormat { get; set; } = ImportFormat.Unknown; //default to las for now
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExportFormat exportFormat { get; set; }

        public List<string> inputFiles { get; set; } = new List<string>();
        public string outputFile { get; set; } = null;

        public List<string> errors { get; set; } = new List<string>(); // return errors to UI

        // FIXME default values will be used unless otherwise specified.. randomize = true 
        // TODO these should be export settings..

        public bool importRGB { get; set; } = true;
        public bool importIntensity { get; set; } = false;
        public bool importClassification { get; set; } = false;
        public bool useAutoOffset { get; set; } = true;
        public bool swapYZ { get; set; } = true;
        public bool invertX { get; set; } = false;
        public bool invertZ { get; set; } = false;
        public float offsetX { get; set; } = 0;
        public float offsetY { get; set; } = 0;
        public float offsetZ { get; set; } = 0;
        public bool useLimit { get; set; } = false;
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
        public bool detectIntensityRange { get; set; } = false; // if true, reads some points from file to detect min/max intensity range 0-255 or 0-65535
        public int seed { get; set; } = -1; // random seed for shuffling
        public int maxThreads { get; set; }

        public bool useJSONLog { get; set; } = false;
        public bool importMetadata { get; set; } = false;
        public bool importMetadataOnly { get; set; } = false;
        public bool averageTimestamp { get; set; } = false; // calculate average timestamp for all points for this tile
        public bool checkoverlap { get; set; } = false; // check if tile overlaps with other tiles (save into pcroot)
        public bool useGrid { get; set; } = false; // required for PCROOT format (will be automatically enabled for v3)
        public string offsetMode { get; set; } = "min"; // TODO use enum: "min" or "legacy" now (legacy is first bounds min only)
        public bool useFilter { get; set; } = false; // filter by distance
        public float filterDistance { get; set; } = 0.5f;

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
            t += "\n readClassification=" + importClassification;
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
            t += "\n importMetadata=" + importMetadata;
            t += "\n importMetadataOnly=" + importMetadataOnly;
            t += "\n averageTimestamp=" + averageTimestamp;
            t += "\n checkoverlap=" + checkoverlap;
            t += "\n useGrid=" + useGrid;
            t += "\n offsetMode=" + offsetMode;
            return t;
        }

        internal string ToJSON()
        {
            return JsonSerializer.Serialize(this);
        }

    }

    // TEST dynamic export formats
    [JsonConverter(typeof(CustomExportFormatConverter))]
    public class ExportFormatModel
    {
        public ExportFormat StaticExportFormat { get; set; } = ExportFormat.Unknown;

        // This will store dynamic formats from plugins
        public string DynamicExportFormat { get; set; }
    }

    public class CustomExportFormatConverter : JsonConverter<ExportFormatModel>
    {
        public override ExportFormatModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string stringValue = reader.GetString();
            var model = new ExportFormatModel();

            // Try to parse it as a known static ExportFormat
            if (Enum.TryParse(typeof(ExportFormat), stringValue, true, out var enumValue))
            {
                model.StaticExportFormat = (ExportFormat)enumValue;
            }
            else
            {
                // If it's not a known enum value, store it as a dynamic format
                model.DynamicExportFormat = stringValue;
            }

            return model;
        }

        public override void Write(Utf8JsonWriter writer, ExportFormatModel value, JsonSerializerOptions options)
        {
            // Serialize based on whether it's a static enum or dynamic value
            if (value.StaticExportFormat != ExportFormat.Unknown)
            {
                writer.WriteStringValue(value.StaticExportFormat.ToString());
            }
            else
            {
                writer.WriteStringValue(value.DynamicExportFormat);
            }
        }
    }
}
