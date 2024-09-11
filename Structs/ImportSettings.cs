// values from commandline arguments

using PointCloudConverter.Logger;
using PointCloudConverter.Readers;
using PointCloudConverter.Structs;
using PointCloudConverter.Writers;
using System.Collections.Concurrent;
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

        public IReader reader = new LAZ(null); // single threaded reader
        //public Dictionary<int?, IReader> Readers { get; set; } = new Dictionary<int?, IReader>();
        public ConcurrentDictionary<int?, IReader> Readers { get; set; } = new ConcurrentDictionary<int?, IReader>();
        public IWriter writer = new UCPC();
        //public Dictionary<int?, IWriter> Writers { get; set; } = new Dictionary<int?, IWriter>();
        //public ConcurrentDictionary<int?, WeakReference<IWriter>> Writers { get; set; } = new ConcurrentDictionary<int?, WeakReference<IWriter>>();
        private readonly ConcurrentBag<IWriter> _writerPool = new ConcurrentBag<IWriter>();
        private readonly ConcurrentDictionary<int?, IWriter> _allocatedWriters = new ConcurrentDictionary<int?, IWriter>();
        private int _maxWriters = 16;

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
            if (!Readers.ContainsKey(taskId))
            {
                Readers[taskId] = new LAZ(taskId);
            }

            //Log.WriteLine(">>>>> Total Readers in dictionary: " + Readers.Count);

            return Readers[taskId];
        }

        private IWriter CreateNewWriter()
        {
            ///Log.WriteLine(">>>>> Creating new writer: "+exportFormat);
            if (exportFormat == ExportFormat.UCPC)
            {
                return new UCPC();
            }
            else if (exportFormat == ExportFormat.PCROOT)
            {
                return new PCROOT(null); // No taskId when creating the pool, it's assigned later
            }
            else
            {
                Log.WriteLine("Writer format not supported: " + exportFormat, LogEvent.Error);
                return null;
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
                //  Log.WriteLine("ReleaseWriter >>> Memory used: " + GC.GetTotalMemory(false));
                // Clean up the writer if necessary
                writer?.Cleanup(0);
                //writer?.Dispose();
                // Return the writer to the pool for reuse
                _writerPool.Add(writer);
                // Log.WriteLine("ReleaseWriter >>> Memory used: " + GC.GetTotalMemory(false));

            }
        }

        //public IWriter GetOrCreateWriter(int? taskId)
        //{
        //    if (!Writers.ContainsKey(taskId))
        //    {
        //        Writers[taskId] = new PCROOT(taskId);
        //    }

        //    //Log.WriteLine(">>>>> Total Writers in dictionary: " + Writers.Count);

        //    return Writers[taskId];
        //}

        //public void ReleaseReader(int? taskId)
        //{
        //    //Log.WriteLine(">>>>> Releasing reader for task ID: " + taskId);
        //    if (Readers.ContainsKey(taskId))
        //    {
        //        Readers[taskId]?.Close();
        //        //Readers[taskId]?.Dispose(); // FIXME causes exceptions
        //        Readers.Remove(taskId);
        //    }
        //}

        //public IWriter GetOrCreateWriter(int? taskId)
        //{
        //    if (!Writers.TryGetValue(taskId, out var weakWriter) || !weakWriter.TryGetTarget(out var writer))
        //    {
        //        if (exportFormat == ExportFormat.UCPC)
        //        {
        //            writer = new UCPC();
        //        }
        //        else if (exportFormat == ExportFormat.PCROOT)
        //        {
        //            writer = new PCROOT(taskId);
        //        }
        //        else
        //        {
        //            Log.WriteLine("Writer format not supported: " + exportFormat, LogEvent.Error);
        //            writer = null;
        //        }

        //        Writers[taskId] = new WeakReference<IWriter>(writer);
        //    }

        //    return writer;
        //}

        //public void ReleaseWriter(int? taskId)
        //{
        //    if (taskId.HasValue && Writers.TryRemove(taskId, out var weakWriter))
        //    {
        //        if (weakWriter.TryGetTarget(out var writer))
        //        {
        //            Log.WriteLine("ReleaseWriter >>> Memory used: " + GC.GetTotalMemory(false));
        //            //Log.WriteLine(">>>>> Releasing reader for task ID: " + taskId);
        //            writer?.Cleanup(0);
        //            writer?.Dispose();
        //            Log.WriteLine("ReleaseWriter <<< Memory used: " + GC.GetTotalMemory(false));
        //        }
        //    }
        //}

        public void ReleaseReader(int? taskId)
        {
            // Log the release of the reader for the specified task ID
            // Log.WriteLine(">>>>> Releasing reader for task ID: " + taskId);

            if (taskId.HasValue)
            {
                if (Readers.TryRemove(taskId, out var reader))
                {
                    reader?.Close();
                    // reader?.Dispose();
                }
                else
                {
                    Log.WriteLine($"Reader for task ID {taskId} could not be removed because it was not found.", LogEvent.Warning);
                }
            }
        }

        //public void ReleaseWriter(int? taskId)
        //{
        //    // Log the release of the reader for the specified task ID
        //    // Log.WriteLine(">>>>> Releasing reader for task ID: " + taskId);

        //    if (taskId.HasValue)
        //    {
        //        if (Writers.TryRemove(taskId, out var writer))
        //        {
        //            Log.WriteLine("ReleaseWriter >>> Memory used: " + GC.GetTotalMemory(false));
        //            writer?.Cleanup(0);
        //            writer?.Dispose();
        //            //writer = null;
        //            // clear gc
        //            System.GC.Collect();
        //            //GC.SuppressFinalize(writer);
        //            System.GC.WaitForPendingFinalizers();
        //            Log.WriteLine("ReleaseWriter <<< Memory used: " + GC.GetTotalMemory(false));
        //        }
        //        else
        //        {
        //            Log.WriteLine($"Reader for task ID {taskId} could not be removed because it was not found.", LogEvent.Warning);
        //        }
        //    }
        //}

        //public void ReleaseWriter(int? taskId)
        //{
        //    //Log.WriteLine(">>>>> Releasing writer for task ID: " + taskId);
        //    if (Writers.ContainsKey(taskId))
        //    {
        //        Writers[taskId]?.Cleanup(0);
        //        Writers.Remove(taskId);
        //    }
        //    else
        //    {
        //        //Log.WriteLine("----->>>>> Writer not found in dictionary for task ID: " + taskId);
        //    }
        //}

        public bool haveError { get; set; } = false; // if errors during parsing args
        //public string[] errorMessages = null; // last error message(s)

        public bool useScale { get; set; } = false;
        public float scale { get; set; } = 1f;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ImportFormat importFormat { get; set; } = ImportFormat.LAS; //default to las for now
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExportFormat exportFormat { get; set; }

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
        public int seed { get; set; } = -1; // random seed for shuffling
        public int maxThreads { get; set; }

        public bool useJSONLog = false;
        public bool importMetadata = false;
        public bool importMetadataOnly = false;
        public bool averageTimestamp = false; // calculate average timestamp for all points for this tile
        public bool checkoverlap = false; // check if tile overlaps with other tiles (save into pcroot)

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
            t += "\n importMetadata=" + importMetadata;
            t += "\n importMetadataOnly=" + importMetadataOnly;
            t += "\n averageTimestamp=" + averageTimestamp;
            t += "\n checkoverlap=" + checkoverlap;
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
