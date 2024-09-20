using PointCloudConverterForDotnetCLI;
using PointCloudConverterForDotnetCLI.Logger;
using PointCloudConverterForDotnetCLI.Structs;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Color = PointCloudConverterForDotnetCLI.Structs.Color;
using Newtonsoft.Json;
using System.Threading.Tasks;
using PointCloudConverterForDotnetCLI.Readers;
using System.Collections.Concurrent;
using PointCloudConverterForDotnetCLI.Writers;
using System.Reflection;
using System.Globalization;
using System;
using static System.Net.Mime.MediaTypeNames;
using System.Drawing;


// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
string  _rootFolder = AppDomain.CurrentDomain.BaseDirectory;
string version = "1.0";
string appname = "PointCloud Converter For Dotnet CLI - " + version;

List<LasHeader> lasHeaders = new List<LasHeader>();
string lastStatusMessage = "";
int errorCounter = 0;

CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
string externalFileFormats = "";

Dictionary<string, Type> externalWriters = new Dictionary<string, Type>();
ILogger Log;

// check cmdline args
Tools.FixDLLFoldersAndConfig(_rootFolder);
Tools.ForceDotCultureSeparator();

// default logger
//Log.CreateLogger(isJSON: false, version: version);
Log = LoggerFactory.CreateLogger(isJSON: false);
//Log.CreateLogger(isJSON: false, version: "1.0");
// default code
Environment.ExitCode = (int)ExitCode.Success;

var pluginsDirectory = "plugins";

if (Directory.Exists(pluginsDirectory))
{
    //Log.Write("Plugins directory not found.");

    // Get all DLL files in the plugins directory
    var pluginFiles = Directory.GetFiles(pluginsDirectory, "*.dll");

    foreach (var pluginDLL in pluginFiles)
    {
        try
        {
            // Load the DLL file as an assembly
            var assembly = Assembly.LoadFrom(pluginDLL);

            // Find all types in the assembly that implement IWriter
            var writerTypes = assembly.GetTypes().Where(type => typeof(IWriter).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract);

            foreach (var writerType in writerTypes)
            {
                // Derive a unique key for the writer (e.g., from its name or class name)
                string writerName = writerType.Name;//.Replace("Writer", ""); // Customize the key generation logic
                if (!externalWriters.ContainsKey(writerName))
                {
                    // Add the writer type to the dictionary for later use
                    externalWriters.Add(writerName, writerType);
                    //Log.Write($"Found writer: {writerType.FullName} in {pluginDLL}");

                    // TODO take extensions from plugin? has 2: .glb and .gltf
                    externalFileFormats += "|" + writerName + " (" + writerType.FullName + ")|*." + writerName.ToLower();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading plugin {pluginDLL}: {ex.Message}");
        }
    }
}

int progressPoint = 0;
int progressTotalPoints = 0;
int progressFile = 0;
int progressTotalFiles = 0;

// for debug: print config file location in appdata local here directly
// string configFilePath = System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
// Log.Write("Config file: " + configFilePath);

if(args.Length == 0)
{
    Console.WriteLine("No Args");
    Environment.Exit(Environment.ExitCode);
}

// using from commandline
if (args.Length > 1)
{
    // check if have -jsonlog=true
    foreach (var arg in args)
    {
        if (arg.ToLower().Contains("-json=true"))
        {
            //Log.CreateLogger(isJSON: true, version: version);
            Log = LoggerFactory.CreateLogger(isJSON: true);
        }
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Log.Write("\n::: PointCloudConverterForDotnetCLI :::\n");
    //Console.WriteLine("\n::: " + appname + " :::\n");
    Console.ForegroundColor = ConsoleColor.White;
    

    // check args, null here because we get the args later
    var importSettings = ArgParser.Parse(null, _rootFolder, Log);

    // NOTE was not used?
    //if (importSettings.useJSONLog)
    //{
    //    importSettings.version = version;
    //    Log.SetSettings(importSettings);
    //}

    //if (importSettings.useJSONLog) log.Init(importSettings, version);

    // get elapsed time using time
    var startTime = DateTime.Now;

    // if have files, process them
    if (importSettings.errors.Count == 0)
    {
        // NOTE no background thread from commandline
        var workerParams = new WorkerParams
        {
            ImportSettings = importSettings,
            CancellationToken = _cancellationTokenSource.Token
        };        

        await Task.Run(() => ProcessAllFiles(workerParams));
    }

    // print time
    var endTime = DateTime.Now;
    var elapsed = endTime - startTime;
    string elapsedString = elapsed.ToString(@"hh\h\ mm\m\ ss\s\ ms\m\s");

    // end output
    Log.Write("Exited.\nElapsed: " + elapsedString);
    if (importSettings.useJSONLog)
    {
        Log.Write("{\"event\": \"" + LogEvent.End + "\", \"elapsed\": \"" + elapsedString + "\",\"version\":\"" + version + ",\"errors\":" + errorCounter + "}", LogEvent.End);
    }

    Console.WriteLine(Environment.NewLine);    
    Environment.Exit(Environment.ExitCode);
}

async Task ProcessAllFiles(object workerParamsObject)
{
    var workerParams = (WorkerParams)workerParamsObject;
    var importSettings = workerParams.ImportSettings;
    var cancellationToken = workerParams.CancellationToken;
    // Use cancellationToken to check for cancellation
    if (cancellationToken.IsCancellationRequested)
    {
        Environment.ExitCode = (int)ExitCode.Cancelled;
        return;
    }

    Stopwatch stopwatch = new Stopwatch();
    stopwatch.Start();

    // if user has set maxFiles param, loop only that many files
    importSettings.maxFiles = importSettings.maxFiles > 0 ? importSettings.maxFiles : importSettings.inputFiles.Count;
    importSettings.maxFiles = Math.Min(importSettings.maxFiles, importSettings.inputFiles.Count);    

    // loop input files
    errorCounter = 0;

    progressFile = 0;
    progressTotalFiles = importSettings.maxFiles - 1;
    if (progressTotalFiles < 0) progressTotalFiles = 0;

    List<Float3> boundsListTemp = new List<Float3>();

    // get all file bounds, if in batch mode and RGB+INT+PACK
    // TODO: check what happens if its too high? over 128/256?
    //if (importSettings.useAutoOffset == true && importSettings.importIntensity == true && importSettings.importRGB == true && importSettings.packColors == true && importSettings.importMetadataOnly == false)

    //Log.Write(importSettings.useAutoOffset + " && " + importSettings.importMetadataOnly + " || (" + importSettings.importIntensity + " && " + importSettings.importRGB + " && " + importSettings.packColors + " && " + importSettings.importMetadataOnly + ")");
    //bool istrue1 = (importSettings.useAutoOffset == true && importSettings.importMetadataOnly == false);
    //bool istrue2 = (importSettings.importIntensity == true && importSettings.importRGB == true && importSettings.packColors == true && importSettings.importMetadataOnly == false);
    //Log.Write(istrue1 ? "1" : "0");
    //Log.Write(istrue2 ? "1" : "0");

    if ((importSettings.useAutoOffset == true && importSettings.importMetadataOnly == false) || (importSettings.importIntensity == true && importSettings.importRGB == true && importSettings.packColors == true && importSettings.importMetadataOnly == false))
    {
        for (int i = 0, len = importSettings.maxFiles; i < len; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return; // Exit the loop if cancellation is requested
            }

            progressFile = i;
            Log.Write("\nReading bounds from file (" + (i + 1) + "/" + len + ") : " + importSettings.inputFiles[i] + " (" + Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[i]).Length) + ")");
            var res = GetBounds(importSettings, i);

            if (res.Item1 == true)
            {
                boundsListTemp.Add(new Float3(res.Item2, res.Item3, res.Item4));
            }
            else
            {
                errorCounter++;
                if (importSettings.useJSONLog)
                {
                    Log.Write("{\"event\": \"" + LogEvent.File + "\", \"path\": " + System.Text.Json.JsonSerializer.Serialize(importSettings.inputFiles[i]) + ", \"status\": \"" + LogStatus.Processing + "\"}", LogEvent.Error);
                }
                else
                {
                    Log.Write("Error> Failed to get bounds from file: " + importSettings.inputFiles[i], LogEvent.Error);
                }
            }
        }

        // find lowest bounds from boundsListTemp
        float lowestX = float.MaxValue;
        float lowestY = float.MaxValue;
        float lowestZ = float.MaxValue;
        for (int iii = 0; iii < boundsListTemp.Count; iii++)
        {
            if (boundsListTemp[iii].x < lowestX) lowestX = (float)boundsListTemp[iii].x;
            if (boundsListTemp[iii].y < lowestY) lowestY = (float)boundsListTemp[iii].y;
            if (boundsListTemp[iii].z < lowestZ) lowestZ = (float)boundsListTemp[iii].z;
        }

        //Console.WriteLine("Lowest bounds: " + lowestX + " " + lowestY + " " + lowestZ);
        // TODO could take center for XZ, and lowest for Y?
        importSettings.offsetX = lowestX;
        importSettings.offsetY = lowestY;
        importSettings.offsetZ = lowestZ;
    } // if useAutoOffset

    lasHeaders.Clear();
    progressFile = 0;

    //for (int i = 0, len = importSettings.maxFiles; i < len; i++)
    //{
    //    if (cancellationToken.IsCancellationRequested)
    //    {
    //        return; // Exit the loop if cancellation is requested
    //    }

    //    progressFile = i;
    //    Log.Write("\nReading file (" + (i + 1) + "/" + len + ") : " + importSettings.inputFiles[i] + " (" + Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[i]).Length) + ")");
    //    //Debug.WriteLine("\nReading file (" + (i + 1) + "/" + len + ") : " + importSettings.inputFiles[i] + " (" + Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[i]).Length) + ")");
    //    //if (abort==true) 
    //    // do actual point cloud parsing for this file
    //    var res = ParseFile(importSettings, i);
    //    if (res == false)
    //    {
    //        errorCounter++;
    //        if (importSettings.useJSONLog)
    //        {
    //            Log.Write("{\"event\": \"" + LogEvent.File + "\", \"path\": " + System.Text.Json.JsonSerializer.Serialize(importSettings.inputFiles[i]) + ", \"status\": \"" + LogStatus.Processing + "\"}", LogEvent.Error);
    //        }
    //        else
    //        {
    //            Log.Write("Error> Failed to parse file: " + importSettings.inputFiles[i], LogEvent.Error);
    //        }
    //    }
    //}
    //// hack to fix progress bar not updating on last file
    //progressFile++;

    // clamp to maxfiles
    int maxThreads = Math.Min(importSettings.maxThreads, importSettings.maxFiles);
    // clamp to min 1
    maxThreads = Math.Max(maxThreads, 1);
    Log.Write("Using MaxThreads: " + maxThreads);

    // init pool
    importSettings.InitWriterPool(maxThreads, importSettings.exportFormat);

    var semaphore = new SemaphoreSlim(maxThreads);

    var tasks = new List<Task>();


    for (int i = 0, len = importSettings.maxFiles; i < len; i++)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        //await semaphore.WaitAsync(cancellationToken);
        try
        {
            await semaphore.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Handle the cancellation scenario here
            Log.Write("Wait was canceled.");
        }
        finally
        {
            //// Ensure the semaphore is released, if needed
            //if (semaphore.CurrentCount == 0) // Make sure we don't release more times than we acquire
            //{
            //    semaphore.Release();
            //}
        }
        //int? taskId = Task.CurrentId; // Get the current task ID

        //progressFile = i;
        Interlocked.Increment(ref progressFile);

        //bool isLastTask = (i == len - 1); // Check if this is the last task

        int index = i; // Capture the current file index in the loop
        int len2 = len;
        tasks.Add(Task.Run(async () =>
        {
            int? taskId = Task.CurrentId; // Get the current task ID
                                          //Log.Write("task started: " + taskId + " fileindex: " + index);
            Log.Write("task:" + taskId + ", reading file (" + (index + 1) + "/" + len2 + ") : " + importSettings.inputFiles[index] + " (" + Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[index]).Length) + ")\n");

            try
            {
                // Do actual point cloud parsing for this file and pass taskId
                var res = ParseFile(importSettings, index, taskId, cancellationToken);
                if (!res)
                {
                    Interlocked.Increment(ref errorCounter); // thread-safe error counter increment
                    if (importSettings.useJSONLog)
                    {
                        //Trace.WriteLine("useJSONLoguseJSONLoguseJSONLoguseJSONLog");
                        Log.Write("{\"event\": \"" + LogEvent.File + "\", \"path\": " + System.Text.Json.JsonSerializer.Serialize(importSettings.inputFiles[i]) + ", \"status\": \"" + LogStatus.Processing + "\"}", LogEvent.Error);
                    }
                    else
                    {
                        Log.Write("Error> Failed to parse file: " + importSettings.inputFiles[i], LogEvent.Error);
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                Log.Write("Task was canceled: " + ex.Message, LogEvent.Error);
            }
            catch (TimeoutException ex)
            {
                Log.Write("Timeout occurred: " + ex.Message, LogEvent.Error);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Operation was canceled.");
            }
            catch (Exception ex)
            {
                Log.Write("Exception> " + ex.Message, LogEvent.Error);
                //throw; // Rethrow to ensure Task.WhenAll sees the exception
            }
            finally
            {
                semaphore.Release(); // Release the semaphore slot when the task is done
            }
        }));
    } // for all files

    await Task.WhenAll(tasks); // Wait for all tasks to complete

    //Trace.WriteLine(" ---------------------- all finished -------------------- ");

    // now write header for for pcroot (using main writer)
    if (importSettings.exportFormat != ExportFormat.UCPC)
    {
        importSettings.writer.Close();
        // UCPC calls close in Save() itself
    }

    // if this was last file
    //if (fileIndex == (importSettings.maxFiles - 1))
    //            {
    JsonSerializerSettings settings = new JsonSerializerSettings
    {
        StringEscapeHandling = StringEscapeHandling.Default // This prevents escaping of characters and write the WKT string properly
    };

    string jsonMeta = JsonConvert.SerializeObject(lasHeaders, settings);

    // var jsonMeta = JsonSerializer.Serialize(lasHeaders, new JsonSerializerOptions() { WriteIndented = true });
    //Log.Write("MetaData: " + jsonMeta);
    // write metadata to file
    if (importSettings.importMetadata == true)
    {
        var jsonFile = Path.Combine(Path.GetDirectoryName(importSettings.outputFile), Path.GetFileNameWithoutExtension(importSettings.outputFile) + ".json");
        Log.Write("Writing metadata to file: " + jsonFile);
        File.WriteAllText(jsonFile, jsonMeta);
    }

    lastStatusMessage = "Done!";
    Console.ForegroundColor = ConsoleColor.Green;
    Log.Write("Finished!");
    Console.ForegroundColor = ConsoleColor.White;    
        
    var dir = Path.GetDirectoryName(importSettings.outputFile);
    if (Directory.Exists(dir))
    {
        var psi = new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true,
            Verb = "open"
        };
        Process.Start(psi);
    }    

    stopwatch.Stop();
    Log.Write("Elapsed: " + (TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds)).ToString(@"hh\h\ mm\m\ ss\s\ ms\m\s"));
    stopwatch.Reset();    
}

(bool, float, float, float) GetBounds(ImportSettings importSettings, int fileIndex)
{
    var res = importSettings.reader.InitReader(importSettings, fileIndex);
    if (res == false)
    {
        Log.Write("Unknown error while initializing reader: " + importSettings.inputFiles[fileIndex]);
        Environment.ExitCode = (int)ExitCode.Error;
        return (false, 0, 0, 0);
    }
    var bounds = importSettings.reader.GetBounds();
    //Console.WriteLine(bounds.minX + " " + bounds.minY + " " + bounds.minZ);

    importSettings.reader.Close();

    return (true, bounds.minX, bounds.minY, bounds.minZ);
}

bool ParseFile(ImportSettings importSettings, int fileIndex, int? taskId, CancellationToken cancellationToken)
{
    progressTotalPoints = 0;

    Log.Write("Started processing file: " + importSettings.inputFiles[fileIndex]);

    // each thread needs its own reader
    bool res;

    //importSettings.reader = new LAZ(taskId);
    IReader taskReader = importSettings.GetOrCreateReader(taskId);
    

    try
    {
        res = taskReader.InitReader(importSettings, fileIndex);
    }
    catch (Exception)
    {
        throw new Exception("Error> Failed to initialize reader: " + importSettings.inputFiles[fileIndex]);
    }

    //Log.Write("taskid: " + taskId + " reader initialized");

    if (res == false)
    {
        Log.Write("Unknown error while initializing reader: " + importSettings.inputFiles[fileIndex]);
        Environment.ExitCode = (int)ExitCode.Error;
        return false;
    }

    if (importSettings.importMetadata == true)
    {
        var metaData = taskReader.GetMetaData(importSettings, fileIndex);
        lasHeaders.Add(metaData);
    }

    if (importSettings.importMetadataOnly == false)
    {
        int fullPointCount = taskReader.GetPointCount();
        int pointCount = fullPointCount;

        // show stats for decimations
        if (importSettings.skipPoints == true)
        {
            var afterSkip = (int)Math.Floor(pointCount - (pointCount / (float)importSettings.skipEveryN));
            Log.Write("Skip every X points is enabled, original points: " + fullPointCount + ", After skipping:" + afterSkip);
            pointCount = afterSkip;
        }

        if (importSettings.keepPoints == true)
        {
            Log.Write("Keep every x points is enabled, original points: " + fullPointCount + ", After keeping:" + (pointCount / importSettings.keepEveryN));
            pointCount = pointCount / importSettings.keepEveryN;
        }

        if (importSettings.useLimit == true)
        {
            Log.Write("Original points: " + pointCount + " Limited points: " + importSettings.limit);
            pointCount = importSettings.limit > pointCount ? pointCount : importSettings.limit;
        }
        else
        {
            Log.Write("Points: " + pointCount + " (" + importSettings.inputFiles[fileIndex] + ")");
        }

        // NOTE only works with formats that have bounds defined in header, otherwise need to loop whole file to get bounds?

        // dont use these bounds, in this case
        if (importSettings.useAutoOffset == true || (importSettings.importIntensity == true && importSettings.importRGB == true && importSettings.packColors == true))
        {
            // we use global bounds or Y offset to fix negative Y
        }
        else if (importSettings.useManualOffset == true)
        {
            importSettings.offsetX = importSettings.manualOffsetX;
            importSettings.offsetY = importSettings.manualOffsetY;
            importSettings.offsetZ = importSettings.manualOffsetZ;
        }
        else // neither
        {
            importSettings.offsetX = 0;
            importSettings.offsetY = 0;
            importSettings.offsetZ = 0;
        }

        var taskWriter = importSettings.GetOrCreateWriter(taskId);

        // for saving pcroot header, we need this writer
        if (importSettings.exportFormat != ExportFormat.UCPC)
        {
            var mainWriterRes = importSettings.writer.InitWriter(importSettings, pointCount, Log);
            if (mainWriterRes == false)
            {
                Log.Write("Error> Failed to initialize main Writer, fileindex: " + fileIndex + " taskid:" + taskId);
                return false;
            }
        }

        // init writer for this file
        var writerRes = taskWriter.InitWriter(importSettings, pointCount, Log);
        if (writerRes == false)
        {
            Log.Write("Error> Failed to initialize Writer, fileindex: " + fileIndex + " taskid:" + taskId);
            return false;
        }

        lastStatusMessage = "Processing points..";

        string jsonString = "{" +
        "\"event\": \"" + LogEvent.File + "\"," +
        "\"path\": " + System.Text.Json.JsonSerializer.Serialize(importSettings.inputFiles[fileIndex]) + "," +
        "\"size\": " + new FileInfo(importSettings.inputFiles[fileIndex]).Length + "," +
        "\"points\": " + pointCount + "," +
        "\"status\": \"" + LogStatus.Processing + "\"" +
        "}";

        Log.Write(jsonString, LogEvent.File);

        int checkCancelEvery = fullPointCount / 128;

        // Loop all points

        int maxPointIterations = importSettings.useLimit ? pointCount : fullPointCount;

        //for (int i = 0; i < fullPointCount; i++)
        for (int i = 0; i < maxPointIterations; i++)
        //for (int i = 0; i < 1000; i++)
        {

            // stop at limit count
            //if (importSettings.useLimit == true && i > pointCount) break;

            // check for cancel every 1% of points
            if (i % checkCancelEvery == 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    //Log.Write("Parse task (" + taskId + ") was canceled for: " + importSettings.inputFiles[fileIndex]);
                    return false;
                }
            }

            // FIXME: need to add skip and keep point skipper here, to make skipping faster!

            // get point XYZ
            Float3 point = taskReader.GetXYZ();
            if (point.hasError == true) break; // TODO display errors

            // add offsets (its 0 if not used)
            point.x -= importSettings.offsetX;
            point.y -= importSettings.offsetY;
            point.z -= importSettings.offsetZ;

            // scale if enabled
            //point.x = importSettings.useScale ? point.x * importSettings.scale : point.x;
            //point.y = importSettings.useScale ? point.y * importSettings.scale : point.y;
            //point.z = importSettings.useScale ? point.z * importSettings.scale : point.z;
            if (importSettings.useScale == true)
            {
                point.x *= importSettings.scale;
                point.y *= importSettings.scale;
                point.z *= importSettings.scale;
            }

            // flip if enabled
            if (importSettings.swapYZ == true)
            {
                var temp = point.z;
                point.z = point.y;
                point.y = temp;
            }

            // flip Z if enabled
            if (importSettings.invertZ == true)
            {
                point.z = -point.z;
            }

            // flip X if enabled
            if (importSettings.invertX == true)
            {
                point.x = -point.x;
            }

            // get point color
            Color rgb = (default);
            Color intensity = (default);
            double time = 0;

            if (importSettings.importRGB == true)
            {
                rgb = taskReader.GetRGB();
            }

            // TODO get intensity as separate value, TODO is this float or rgb?
            if (importSettings.importIntensity == true)
            {
                intensity = taskReader.GetIntensity();
                //if (i < 100) Console.WriteLine(intensity.r);

                // if no rgb, then replace RGB with intensity
                if (importSettings.importRGB == false)
                {
                    rgb.r = intensity.r;
                    rgb.g = intensity.r;
                    rgb.b = intensity.r;
                }
            }

            if (importSettings.averageTimestamp == true)
            {
                // get time
                time = taskReader.GetTime();
                //Console.WriteLine("Time: " + time);
            }

            // collect this point XYZ and RGB into node, optionally intensity also
            //importSettings.writer.AddPoint(i, (float)point.x, (float)point.y, (float)point.z, rgb.r, rgb.g, rgb.b, importSettings.importIntensity, intensity.r, importSettings.averageTimestamp, time);
            // TODO can remove importsettings, its already passed on init
            taskWriter.AddPoint(i, (float)point.x, (float)point.y, (float)point.z, rgb.r, rgb.g, rgb.b, importSettings.importIntensity, intensity.r, importSettings.averageTimestamp, time);                        

        } // for all points                

        lastStatusMessage = "Saving files..";
        //importSettings.writer.Save(fileIndex);
        taskWriter.Save(fileIndex);
        lastStatusMessage = "Finished saving..";
        //taskReader.Close();

        //Log.Write("------------ release reader and writer ------------");
        importSettings.ReleaseReader(taskId);
        //taskReader.Dispose();
        importSettings.ReleaseWriter(taskId);
        //Log.Write("------------ reader and writer released ------------");

        // TODO add event for finished writing this file, and return list of output files
        //jsonString = "{" +
        //    "\"event\": \"" + LogEvent.File + "\"," +
        //    "\"path\": " + System.Text.Json.JsonSerializer.Serialize(importSettings.inputFiles[fileIndex]) + "," +
        //    //"\"size\": " + new FileInfo(importSettings.inputFiles[fileIndex]).Length + "," +
        //    //"\"points\": " + pointCount + "," +
        //    "\"status\": \"" + LogStatus.Complete + "\"" +
        //    "}";

        //Log.Write(jsonString, LogEvent.File);

    } // if importMetadataOnly == false

    //Log.Write("taskid: " + taskId + " done");
    return true;
} // ParseFile



public class WorkerParams
{
    public ImportSettings ImportSettings { get; set; }
    public CancellationToken CancellationToken { get; set; }
}

