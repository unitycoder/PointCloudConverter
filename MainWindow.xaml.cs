// Standalone Point Cloud Converter https://github.com/unitycoder/PointCloudConverter

using PointCloudConverter.Logger;
using PointCloudConverter.Structs;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = PointCloudConverter.Structs.Color;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Newtonsoft.Json;
using Brushes = System.Windows.Media.Brushes;
using System.Threading.Tasks;
using PointCloudConverter.Readers;
using System.Collections.Concurrent;
using PointCloudConverter.Writers;
using System.Reflection;
using System.Globalization;
using System.Windows.Media;
using PointCloudConverter.Structs.Metadata;

namespace PointCloudConverter
{
    public partial class MainWindow : Window
    {
        static readonly string version = "27.08.2025";
        static readonly string appname = "PointCloud Converter - " + version;
        static readonly string rootFolder = AppDomain.CurrentDomain.BaseDirectory;

        // allow console output from WPF application https://stackoverflow.com/a/7559336/5452781
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);
        const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;

        // detach from console, otherwise file is locked https://stackoverflow.com/a/29572349/5452781
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeConsole();

        const uint WM_CHAR = 0x0102;
        const int VK_ENTER = 0x0D;

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static MainWindow mainWindowStatic;
        bool isInitialiazing = true;

        static JobMetadata jobMetadata = new JobMetadata();

        // progress bar data
        static int progressPoint = 0;
        static int progressTotalPoints = 0;
        static int progressFile = 0;
        static int progressTotalFiles = 0;
        static DispatcherTimer progressTimerThread;
        public static string lastStatusMessage = "";
        public static int errorCounter = 0; // how many errors when importing or reading files (single file could have multiple errors)
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        // filter by distance
        private readonly float cellSize = 0.5f;
        private static ConcurrentDictionary<(int, int, int), byte> occupiedCells = new();

        // plugins
        string externalFileFormats = "";

        public MainWindow()
        {
            InitializeComponent();
            mainWindowStatic = this;
            Main();
        }

        public static Dictionary<string, Type> externalWriters = new Dictionary<string, Type>();

        static ILogger Log;

        private async void Main()
        {
            // check cmdline args
            string[] args = Environment.GetCommandLineArgs();
            Tools.FixDLLFoldersAndConfig(rootFolder);
            Tools.ForceDotCultureSeparator();

            // default logger
            //Log.CreateLogger(isJSON: false, version: version);
            Log = LoggerFactory.CreateLogger(isJSON: false);
            //Log.CreateLogger(isJSON: false, version: "1.0");
            // default code
            Environment.ExitCode = (int)ExitCode.Success;

            // load all plugins from plugins folder
            //var testwriter = PointCloudConverter.Plugins.PluginLoader.LoadWriter("plugins/GLTFWriter.dll");
            ////testwriter.Close();
            //externalWriters = AppDomain.CurrentDomain.GetAssemblies().SelectMany(assembly => assembly.GetTypes()).Where(type => typeof(IWriter).IsAssignableFrom(type) && !type.IsInterface);

            // Get the directory of the running executable
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Build absolute path to plugins folder
            var pluginsDirectory = Path.Combine(exeDir, "plugins");

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
                    catch (ReflectionTypeLoadException rex)
                    {
                        Log.Write($"Error loading plugin {pluginDLL}: {rex.Message}");

                        foreach (var loaderException in rex.LoaderExceptions)
                        {
                            Log.Write(" - " + loaderException?.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write($"General error loading plugin {pluginDLL}: {ex.Message}");
                    }
                }
            } // if plugins folder exists

            //return;

            // for debug: print config file location in appdata local here directly
            // string configFilePath = System.Configuration.ConfigurationManager.OpenExeConfiguration(System.Configuration.ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            // Log.Write("Config file: " + configFilePath);

            // using from commandline
            if (args.Length > 1)
            {
                // hide window
                this.Visibility = Visibility.Hidden;

                AttachConsole(ATTACH_PARENT_PROCESS);

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
                Log.Write("\n::: " + appname + " :::\n");
                //Console.WriteLine("\n::: " + appname + " :::\n");
                Console.ForegroundColor = ConsoleColor.White;
                IntPtr cw = GetConsoleWindow();

                // check args, null here because we get the args later
                var importSettings = ArgParser.Parse(null, rootFolder, Log);

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

                    InitProgressBars(importSettings);

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

                // https://stackoverflow.com/a/45620138/5452781
                SendMessage(cw, WM_CHAR, (IntPtr)VK_ENTER, IntPtr.Zero);

                FreeConsole();
                Environment.Exit(Environment.ExitCode);
            }

            // regular WPF starts from here
            this.Title = appname;

            // disable accesskeys without alt
            CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope = true;

            LoadSettings();
        }

        // main processing loop
        private static async Task ProcessAllFiles(object workerParamsObject)
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

            StartProgressTimer();

            // loop input files
            errorCounter = 0;

            progressFile = 0;
            progressTotalFiles = importSettings.maxFiles - 1;
            if (progressTotalFiles < 0) progressTotalFiles = 0;

            List<Float3> boundsListTemp = new List<Float3>();

            // clear filter by distance
            occupiedCells.Clear();

            // get all file bounds, if in batch mode and RGB+INT+PACK
            // TODO: check what happens if its too high? over 128/256?
            //if (importSettings.useAutoOffset == true && importSettings.importIntensity == true && importSettings.importRGB == true && importSettings.packColors == true && importSettings.importMetadataOnly == false)
            //Log.Write(importSettings.useAutoOffset + " && " + importSettings.importMetadataOnly + " || (" + importSettings.importIntensity + " && " + importSettings.importRGB + " && " + importSettings.packColors + " && " + importSettings.importMetadataOnly + ")");
            //bool istrue1 = (importSettings.useAutoOffset == true && importSettings.importMetadataOnly == false);
            //bool istrue2 = (importSettings.importIntensity == true && importSettings.importRGB == true && importSettings.packColors == true && importSettings.importMetadataOnly == false);
            //Log.Write(istrue1 ? "1" : "0");
            //Log.Write(istrue2 ? "1" : "0");

            if ((importSettings.useAutoOffset == true && importSettings.importMetadataOnly == false) || ((importSettings.importIntensity == true || importSettings.importClassification == true) && importSettings.importRGB == true && importSettings.packColors == true && importSettings.importMetadataOnly == false))
            {
                int iterations = importSettings.offsetMode == "min" ? importSettings.maxFiles : 1; // 1 for legacy mode (first cloud only)

                for (int i = 0, len = iterations; i < len; i++)
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

                // NOTE this fails with some files? returns 0,0,0 for some reason
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

                //Log.Write("Lowest bounds: " + lowestX + " " + lowestY + " " + lowestZ);
                // TODO could take center for XZ, and lowest for Y?
                importSettings.offsetX = lowestX;
                importSettings.offsetY = lowestY;
                importSettings.offsetZ = lowestZ;
            } // if useAutoOffset


            //lasHeaders.Clear();
            jobMetadata.Job = new Job
            {
                ConverterVersion = version,
                ImportSettings = importSettings,
                StartTime = DateTime.Now
            };
            jobMetadata.lasHeaders.Clear();
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
                await semaphore.WaitAsync(cancellationToken);     // acquire BEFORE starting task
                int index = i;                                     // capture
                tasks.Add(Task.Run(() =>
                {
                    int? taskId = Task.CurrentId;                  // may be null; your pool should accept null

                    try
                    {
                        Log.Write($"task:{taskId}, reading file ({index + 1}/{len}) : " +
                                  $"{importSettings.inputFiles[index]} ({Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[index]).Length)})\n");

                        var ok = ParseFile(importSettings, index, taskId, cancellationToken);
                        if (!ok)
                        {
                            Interlocked.Increment(ref errorCounter);
                            if (cancellationToken.IsCancellationRequested)
                            {
                                Log.Write("Task was canceled.");
                            }
                            else if (importSettings.useJSONLog)
                            {
                                Log.Write(
                                    "{\"event\":\"" + LogEvent.File + "\",\"path\":" +
                                    System.Text.Json.JsonSerializer.Serialize(importSettings.inputFiles[index]) +
                                    ",\"status\":\"" + LogStatus.Processing + "\"}", LogEvent.Error);
                            }
                            else
                            {
                                Log.Write("files" + importSettings.inputFiles.Count + " i:" + index);
                                Log.Write("Error> Failed to parse file: " + importSettings.inputFiles[index], LogEvent.Error);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Write("Operation was canceled.");
                    }
                    catch (TimeoutException ex)
                    {
                        Log.Write("Timeout occurred: " + ex.Message, LogEvent.Error);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Exception> " + ex.Message, LogEvent.Error);
                    }
                    finally
                    {
                        Interlocked.Increment(ref progressFile);
                        // make sure we don't keep heavy buffers in the pools.
                        //try { importSettings.ReleaseReader(taskId); } catch { }
                        //try { importSettings.ReleaseWriter(taskId); } catch { }

                        // release exactly once per WaitAsync
                        semaphore.Release();                       
                    }
                }, cancellationToken));
            }

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

            // add job date
            jobMetadata.Job.EndTime = DateTime.Now;
            jobMetadata.Job.Elapsed = jobMetadata.Job.EndTime - jobMetadata.Job.StartTime;

            string jsonMeta = JsonConvert.SerializeObject(jobMetadata, settings);

            // var jsonMeta = JsonSerializer.Serialize(lasHeaders, new JsonSerializerOptions() { WriteIndented = true });
            //Log.Write("MetaData: " + jsonMeta);
            // write metadata to file
            if (importSettings.importMetadata == true)
            {
                string filename = Path.GetFileNameWithoutExtension(importSettings.outputFile);
                // for gltf, there is no output filename
                if (string.IsNullOrEmpty(filename))
                {
                    // get last folder name
                    filename = Path.GetFileName(Path.GetDirectoryName(importSettings.inputFiles[0]));
                }
                var jsonFile = Path.Combine(Path.GetDirectoryName(importSettings.outputFile), filename + ".json");
                Log.Write("Writing metadata to file: " + jsonFile);
                File.WriteAllText(jsonFile, jsonMeta);
            }

            lastStatusMessage = "Done!";
            Console.ForegroundColor = ConsoleColor.Green;
            Log.Write("Finished!");
            Console.ForegroundColor = ConsoleColor.White;
            mainWindowStatic.Dispatcher.Invoke(() =>
            {
                if ((bool)mainWindowStatic.chkOpenOutputFolder.IsChecked)
                {
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
                }
            });
            //    } // if last file

            stopwatch.Stop();
            Log.Write("Elapsed: " + (TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds)).ToString(@"hh\h\ mm\m\ ss\s\ ms\m\s"));
            stopwatch.Reset();

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                mainWindowStatic.HideProcessingPanel();
                // call update one more time
                ProgressTick(null, null);
                // clear timer
                progressTimerThread.Tick -= ProgressTick;
                progressTimerThread.Stop();
                progressTimerThread = null;
                mainWindowStatic.progressBarFiles.Foreground = Brushes.Green;
                //mainWindowStatic.progressBarPoints.Foreground = Brushes.Green;
            }));
        } // ProcessAllFiles


        void HideProcessingPanel()
        {
            gridProcessingPanel.Visibility = Visibility.Hidden;
        }

        static void StartProgressTimer()
        {
            //Log.Write("Starting progress timer..*-*************************");
            progressTimerThread = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher);
            progressTimerThread.Tick += ProgressTick;
            progressTimerThread.Interval = TimeSpan.FromSeconds(1);
            progressTimerThread.Start();

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                mainWindowStatic.progressBarFiles.Foreground = Brushes.Red;
                //mainWindowStatic.progressBarPoints.Foreground = Brushes.Red;
                mainWindowStatic.lblStatus.Content = "";
            }));
        }

        private static List<ProgressInfo> progressInfos = new List<ProgressInfo>();
        private static object lockObject = new object();

        public class ProgressInfo
        {
            public int Index { get; internal set; }        // Index of the ProgressBar in the UI
            public long CurrentValue { get; internal set; } // Current progress value
            public long MaxValue { get; internal set; }     // Maximum value for the progress
            public string FilePath { get; internal set; }
            public bool UseJsonLog { get; internal set; }
        }

        static void InitProgressBars(ImportSettings importSettings)
        {
            ClearProgressBars();

            int threadCount = importSettings.maxThreads;
            // clamp to max files
            threadCount = Math.Min(threadCount, importSettings.inputFiles.Count);
            threadCount = Math.Max(threadCount, 1);

            //Log.WriteLine("Creating progress bars: " + threadCount);
            bool useJsonLog = importSettings.useJSONLog;
            progressInfos.Clear();

            for (int i = 0; i < threadCount; i++)
            {
                ProgressBar newProgressBar = new ProgressBar
                {
                    Height = 10,
                    Width = 490 / threadCount,
                    Value = 0,
                    Maximum = 100, // TODO set value in parsefile?
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(1, 0, 1, 0),
                    Foreground = Brushes.Red,
                    Background = null,
                    //BorderBrush = Brushes.Red,
                    //ToolTip = $"Thread {i}"
                };

                // Initialize ProgressInfo for each ProgressBar
                var progressInfo = new ProgressInfo
                {
                    Index = i,           // Index in the StackPanel
                    CurrentValue = 0,    // Initial value
                    MaxValue = 100,
                    UseJsonLog = useJsonLog
                };

                progressInfos.Add(progressInfo);

                mainWindowStatic.ProgressBarsContainer.Children.Add(newProgressBar);
            }
        }

        static void ClearProgressBars()
        {
            mainWindowStatic.ProgressBarsContainer.Children.Clear();
        }

        static void ProgressTick(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                //if (progressTotalPoints > 0)
                //{
                mainWindowStatic.progressBarFiles.Value = progressFile;
                mainWindowStatic.progressBarFiles.Maximum = progressTotalFiles + 1;
                mainWindowStatic.lblStatus.Content = lastStatusMessage;

                // Update all progress bars based on the current values in the List
                lock (lockObject) // Lock to safely read progressInfos
                {
                    foreach (var progressInfo in progressInfos)
                    {
                        int index = progressInfo.Index;
                        long currentValue = progressInfo.CurrentValue;
                        long maxValue = progressInfo.MaxValue;

                        // Access ProgressBar directly from the StackPanel.Children using its index
                        if (index >= 0 && index < mainWindowStatic.ProgressBarsContainer.Children.Count)
                        {
                            if (mainWindowStatic.ProgressBarsContainer.Children[index] is ProgressBar progressBar)
                            {
                                progressBar.Maximum = maxValue;
                                progressBar.Value = currentValue;
                                progressBar.Foreground = ((currentValue + 1 >= maxValue) ? Brushes.Lime : Brushes.Red); //+1 hack fix
                                                                                                                        //progressBar.ToolTip = $"Thread {index} - {currentValue} / {maxValue}"; // not visible, because modal dialog
                                                                                                                        //Log.Write("ProgressTick: " + index + " " + currentValue + " / " + maxValue);

                                // print json progress
                                if (progressInfo.UseJsonLog) // TODO now same bool value is for each progressinfo..
                                {
                                    string jsonString = "{" +
                                        "\"event\": \"" + LogEvent.Progress + "\"," +
                                        "\"thread\": " + index + "," +
                                        "\"currentPoint\": " + currentValue + "," +
                                        "\"totalPoints\": " + maxValue + "," +
                                        "\"percentage\": " + (int)((currentValue / (float)maxValue) * 100.0) + "," +
                                        "\"file\": " + System.Text.Json.JsonSerializer.Serialize(progressInfo.FilePath) +
                                        "}";
                                    Log.Write(jsonString, LogEvent.Progress);
                                }
                            }
                        }
                    } // foreach progressinfo
                } // lock
                  //}
                  //else //  finished ?
                  //{
                  //    Log.Write("*************** ProgressTick: progressTotalPoints is 0, finishing..");
                  //    mainWindowStatic.progressBarFiles.Value = 0;
                  //    mainWindowStatic.lblStatus.Content = "";

                //    foreach (UIElement element in mainWindowStatic.ProgressBarsContainer.Children)
                //    {
                //        if (element is ProgressBar progressBar)
                //        {
                //            progressBar.Value = 0;
                //            progressBar.Foreground = Brushes.Lime;
                //        }
                //    }
                //}
            });
        } // ProgressTick()


        static (bool, float, float, float) GetBounds(ImportSettings importSettings, int fileIndex)
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

        // process single file
        static bool ParseFile(ImportSettings importSettings, int fileIndex, int? taskId, CancellationToken cancellationToken)
        {
            progressTotalPoints = 0;

            Log.Write("Started processing file: " + importSettings.inputFiles[fileIndex]);

            // each thread needs its own reader
            bool res;

            //importSettings.reader = new LAZ(taskId);
            IReader taskReader = importSettings.GetOrCreateReader(taskId);

            ProgressInfo progressInfo = null;
            //lock (lockObject)
            {
                //Log.Write(progressInfos.Count + " : " + fileIndex, LogEvent.Info);
                progressInfo = progressInfos[fileIndex % progressInfos.Count];
            }

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

            if (importSettings.importMetadataOnly == false)
            {
                long fullPointCount = taskReader.GetPointCount();
                long pointCount = fullPointCount;

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
                if (importSettings.useAutoOffset == true || ((importSettings.importIntensity == true || importSettings.importClassification == true) && importSettings.importRGB == true && importSettings.packColors == true))
                {
                    // TODO add manual offset here still?
                    // we use global bounds or Y offset to fix negative Y
                }
                else if (importSettings.useManualOffset == true)
                {
                    importSettings.offsetX = importSettings.manualOffsetX;
                    importSettings.offsetY = importSettings.manualOffsetY;
                    importSettings.offsetZ = importSettings.manualOffsetZ;
                }
                else // no autooffset either
                {
                    if (importSettings.useAutoOffset == false)
                    {
                        importSettings.offsetX = 0;
                        importSettings.offsetY = 0;
                        importSettings.offsetZ = 0;
                    }
                }

                //Log.Write("************** Offsets: " + importSettings.offsetX + " " + importSettings.offsetY + " " + importSettings.offsetZ);

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

                //progressPoint = 0;
                progressInfo.CurrentValue = 0;
                progressInfo.MaxValue = importSettings.useLimit ? pointCount : fullPointCount;
                progressInfo.FilePath = importSettings.inputFiles[fileIndex];

                lastStatusMessage = "Processing points..";

                string jsonString = "{" +
                "\"event\": \"" + LogEvent.File + "\"," +
                "\"path\": " + System.Text.Json.JsonSerializer.Serialize(importSettings.inputFiles[fileIndex]) + "," +
                "\"size\": " + new FileInfo(importSettings.inputFiles[fileIndex]).Length + "," +
                "\"points\": " + pointCount + "," +
                "\"status\": \"" + LogStatus.Processing + "\"" +
                "}";

                Log.Write(jsonString, LogEvent.File);

                long checkCancelEvery = Math.Max(1, fullPointCount / 128);

                // detect is 0-255 or 0-65535 range
                bool isCustomIntensityRange = false;

                // Loop all points
                // FIXME: would be nicer, if use different STEP value for skip, keep and limit..(to collect points all over the file, not just start)
                long maxPointIterations = importSettings.useLimit ? pointCount : fullPointCount;
                for (int i = 0; i < maxPointIterations; i++)
                {
                    // check for cancel every 1% of points
                    if (i % checkCancelEvery == 0)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            //Log.Write("Parse task (" + taskId + ") was canceled for: " + importSettings.inputFiles[fileIndex]);
                            return false;
                        }
                    }

                    // get point XYZ
                    Float3 point = taskReader.GetXYZ();
                    if (point.hasError == true) break; // TODO display errors

                    // get point color
                    Color rgb = (default);

                    if (importSettings.importRGB == true)
                    {
                        rgb = taskReader.GetRGB();

                        // convert from srg to linear (if your model seems too bright)
                        if (importSettings.sRGB)
                        {
                            rgb.r = Tools.SRGBToLinear(rgb.r);
                            rgb.g = Tools.SRGBToLinear(rgb.g);
                            rgb.b = Tools.SRGBToLinear(rgb.b);
                        }
                    }

                    // skip points
                    if (importSettings.skipPoints == true && (i % importSettings.skipEveryN == 0)) continue;

                    // keep points
                    if (importSettings.keepPoints == true && (i % importSettings.keepEveryN != 0)) continue;

                    // add offsets (its 0 if not used)
                    point.x -= importSettings.offsetX;
                    point.y -= importSettings.offsetY;
                    point.z -= importSettings.offsetZ;

                    // scale if enabled
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

                    // filtering is done after scaling and offsets
                    if (importSettings.useFilter)
                    {
                        var cell = ((int)Math.Floor(point.x / importSettings.filterDistance), (int)Math.Floor(point.y / importSettings.filterDistance), (int)Math.Floor(point.z / importSettings.filterDistance));

                        if (!occupiedCells.TryAdd(cell, 0))
                        {
                            continue; // cell already taken, skip this point
                        }
                    }

                    ushort intensity = 0;
                    byte classification = 0;
                    double time = 0;

                    // TODO get intensity as separate value
                    if (importSettings.importIntensity == true)
                    {
                        //intensity = 0;
                        intensity = taskReader.GetIntensity();

                        //if (i < 20000) Log.Write("int: " + intensity);

                        if (importSettings.detectIntensityRange && isCustomIntensityRange == false)
                        {
                            // check if intensity is 0-255 or 0-65535
                            isCustomIntensityRange = intensity > 255;
                            //Log.Write("Detecting intensity range " + intensity + " " + (isCustomIntensityRange ? "************" : "")+" "+ importSettings.inputFiles[fileIndex]);
                        }

                        // if no rgb, then replace RGB with intensity, NOTE this doesnt work correctly if using detect intensity range! (since raw value is now ushort, can be 0-65k)
                        if (importSettings.importRGB == false)
                        {
                            rgb.r = intensity / 255f; // convert byte to float
                            rgb.g = rgb.r;
                            rgb.b = rgb.r;
                        }
                    }

                    // FIXME cannot have both classification and intensity, because both save into RGB here

                    if (importSettings.importClassification == true)
                    {
                        classification = taskReader.GetClassification();

                        //classification = (byte)255;

                        //if (classification<0 || classification>1) Log.Write("****: " + classification.ToString());

                        //if (i < 100000) Log.Write("class: " + classification.ToString());// + " minClass: " + minClass + " maxClass: " + maxClass);
                        //classification = 0;
                        //if (intensity.r < minInt)
                        //{
                        //    minInt = intensity.r;
                        //    Log.Write("Min: " + minInt + " Max: " + maxInt);
                        //}
                        //if (intensity.r > maxInt)
                        //{
                        //    maxInt = intensity.r;
                        //    Log.Write("Min: " + minInt + " Max: " + maxInt);
                        //}

                        // if no rgb, then replace RGB with intensity
                        if (importSettings.importRGB == false)
                        {
                            rgb.r = classification / 255f;
                            rgb.g = rgb.r;
                            rgb.b = rgb.r;
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
                    taskWriter.AddPoint(index: i, x: (float)point.x, y: (float)point.y, z: (float)point.z, r: rgb.r, g: rgb.g, b: rgb.b, intensity: intensity, time: time, classification: classification);
                    //progressPoint = i;
                    progressInfo.CurrentValue = i;
                } // for all points

                // hack for missing 100% progress
                progressInfo.CurrentValue = maxPointIterations;

                if (importSettings.detectIntensityRange == true)
                {
                    taskWriter.SetIntensityRange(isCustomIntensityRange);
                }

                lastStatusMessage = "Saving files..";
                //importSettings.writer.Save(fileIndex);
                taskWriter.Save(fileIndex);
                lastStatusMessage = "Finished saving..";
                //taskReader.Close();

                if (importSettings.importMetadata == true)
                {
                    var metaData = taskReader.GetMetaData(importSettings, fileIndex);
                    jobMetadata.lasHeaders.Add(metaData);
                }

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


            } // if importMetadataOnly == false ^
            else // only metadata:
            {
                if (importSettings.importMetadata == true)
                {
                    var metaData = taskReader.GetMetaData(importSettings, fileIndex);
                    jobMetadata.lasHeaders.Add(metaData);
                }
            }

            //Log.Write("taskid: " + taskId + " done");
            return true;
        } // ParseFile

        private void btnConvert_Click(object sender, RoutedEventArgs e)
        {
            // reset progress
            progressTotalFiles = 0;
            progressTotalPoints = 0;

            // reset cancel token
            _cancellationTokenSource = new CancellationTokenSource();

            if (ValidateSettings() == true)
            {
                ProgressTick(null, null);
                gridProcessingPanel.Visibility = Visibility.Visible;
                SaveSettings();
                StartProcess();
            }
            else
            {
                Log.Write("Error> Invalid settings, aborting..");
            }

        }

        private bool ValidateSettings()
        {
            bool res = true;
            if (string.IsNullOrEmpty(txtInputFile.Text))
            {
                System.Windows.MessageBox.Show("Please select input file", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (string.IsNullOrEmpty(txtOutput.Text))
            {
                System.Windows.MessageBox.Show("Please select output file", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (cmbImportFormat.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Please select import format", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (cmbExportFormat.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("Please select export format", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return res;
        }

        public class WorkerParams
        {
            public ImportSettings ImportSettings { get; set; }
            public CancellationToken CancellationToken { get; set; }
        }

        void StartProcess(bool doProcess = true)
        {
            // get args from GUI settings, TODO could directly create new import settings..
            var args = new List<string>();

            // add enabled args to list, TODO use binding later?
            string inputFile = txtInputFile.Text;
            string outputFile = txtOutput.Text;

            // add quotes, if contains space in path
            if (inputFile.Contains(" ")) inputFile = "\"" + inputFile + "\"";
            if (outputFile.Contains(" ")) outputFile = "\"" + outputFile + "\"";

            if (cmbImportFormat.SelectedItem != null)
            {
                args.Add("-importformat=" + cmbImportFormat.SelectedItem.ToString());
            }

            args.Add("-input=" + inputFile);

            if (cmbExportFormat.SelectedItem != null)
            {
                args.Add("-exportformat=" + cmbExportFormat.SelectedItem.ToString());
            }

            args.Add("-output=" + outputFile);

            // check if using autooffset
            //if ((bool)chkAutoOffset.IsChecked && !(bool)chkManualOffset.IsChecked)
            if (!(bool)chkManualOffset.IsChecked)
            {
                args.Add("-offset=" + (bool)chkAutoOffset.IsChecked);
            }

            args.Add("-offsetmode=" + txtOffsetMode.Text.ToLower());

            // or manual offset, TODO later should allow using both (first autooffset, then add manual)
            if ((bool)chkManualOffset.IsChecked) args.Add("-offset=" + txtOffsetX.Text + "," + txtOffsetY.Text + "," + txtOffsetZ.Text);

            args.Add("-rgb=" + (bool)chkImportRGB.IsChecked);
            args.Add("-intensity=" + (bool)chkImportIntensity.IsChecked);
            args.Add("-classification=" + (bool)chkImportClassification.IsChecked);


            bool isPCROOT = (cmbExportFormat.SelectedItem.ToString() == "PCROOT");
            bool isGLTF = (cmbExportFormat.SelectedItem.ToString().ToUpper() == "GLTF" || cmbExportFormat.SelectedItem.ToString().ToUpper() == "GLB");
            // cmbExportFormat.SelectedItem?.ToString()?.ToUpper()?.Contains("PCROOT")

            if (isPCROOT == true) args.Add("-gridsize=" + txtGridSize.Text);

            if ((bool)chkUseMinPointCount.IsChecked) args.Add("-minpoints=" + txtMinPointCount.Text);
            if ((bool)chkUseScale.IsChecked) args.Add("-scale=" + txtScale.Text);
            args.Add("-swap=" + (bool)chkSwapYZ.IsChecked);
            if ((bool)chkInvertX.IsChecked) args.Add("-invertX=" + (bool)chkInvertX.IsChecked);
            if ((bool)chkInvertZ.IsChecked) args.Add("-invertZ=" + (bool)chkInvertZ.IsChecked);
            if ((bool)chkPackColors.IsChecked) args.Add("-pack=" + (bool)chkPackColors.IsChecked);
            if ((bool)chkUsePackMagic.IsChecked) args.Add("-packmagic=" + txtPackMagic.Text);
            if ((bool)chkUseMaxImportPointCount.IsChecked) args.Add("-limit=" + txtMaxImportPointCount.Text);
            if ((bool)chkUseSkip.IsChecked) args.Add("-skip=" + txtSkipEvery.Text);
            if ((bool)chkUseKeep.IsChecked) args.Add("-keep=" + txtKeepEvery.Text);
            if ((bool)chkUseMaxFileCount.IsChecked) args.Add("-maxfiles=" + txtMaxFileCount.Text);
            args.Add("-randomize=" + (bool)chkRandomize.IsChecked);
            if ((bool)chkSetRandomSeed.IsChecked) args.Add("-seed=" + txtRandomSeed.Text);
            if ((bool)chkUseJSONLog.IsChecked) args.Add("-json=true");
            if ((bool)chkReadMetaData.IsChecked) args.Add("-metadata=true");
            if ((bool)chkMetaDataOnly.IsChecked) args.Add("-metadataonly=true");
            if ((bool)chkGetAvgTileTimestamp.IsChecked) args.Add("-averagetimestamp=true");
            if ((bool)chkCalculateOverlappingTiles.IsChecked) args.Add("-checkoverlap=true");
            args.Add("-maxthreads=" + txtMaxThreads.Text);

            if ((bool)chkUseFilter.IsChecked) args.Add("-filter=" + txtFilterDistance.Text);
            if ((bool)chkConvertSRGB.IsChecked) args.Add("-srgb=true");


            if (isGLTF == true) args.Add(("-usegrid=" + (bool)chkUseGrid.IsChecked).ToLower());

            if (((bool)chkImportIntensity.IsChecked) && ((bool)chkCustomIntensityRange.IsChecked)) args.Add("-customintensityrange=True");
            if (((bool)chkDetectIntensityRange.IsChecked) && ((bool)chkDetectIntensityRange.IsChecked)) args.Add("-detectintensityrange=True");

            // check input files
            //Trace.WriteLine("loggeris:" + Log.GetType().ToString());

            var importSettings = ArgParser.Parse(args.ToArray(), rootFolder, Log);

            // if have files, process them
            if (importSettings.errors.Count == 0)
            {
                // show output settings for commandline
                var commandLineString = string.Join(" ", args);
                // add executable name at front (exe only, no path, keep extension
                commandLineString = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".exe " + commandLineString;

                txtConsole.Text = commandLineString;
                Console.WriteLine(commandLineString);

                if (doProcess == true)
                {
                    //ParameterizedThreadStart start = new ParameterizedThreadStart(ProcessAllFiles);
                    //workerThread = new Thread(start);
                    //workerThread.IsBackground = true;
                    //workerThread.Start(importSettings);

                    //var workerParams = new WorkerParams
                    //{
                    //    ImportSettings = importSettings,
                    //    CancellationToken = _cancellationTokenSource.Token
                    //};

                    //ParameterizedThreadStart start = new ParameterizedThreadStart(ProcessAllFiles);
                    //workerThread = new Thread(start)
                    //{
                    //    IsBackground = true
                    //};
                    //workerThread.Start(workerParams);
                    var workerParams = new WorkerParams
                    {
                        ImportSettings = importSettings,
                        CancellationToken = _cancellationTokenSource.Token
                    };

                    InitProgressBars(importSettings);

                    Task.Run(() => ProcessAllFiles(workerParams));
                }
            }
            else
            {
                HideProcessingPanel();
                txtConsole.Text = "Operation failed! " + string.Join(Environment.NewLine, importSettings.errors);
                Environment.ExitCode = (int)ExitCode.Error;
            }
        }

        // set gui from commandline args
        void ImportArgs(string rawArgs)
        {
            Log.Write(rawArgs);
            string[] args = ArgParser.SplitArgs(rawArgs);
            bool isFirstArgExe = args[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            int startIndex = isFirstArgExe ? 1 : 0;

            // reset all checkboxes to false
            UncheckAllCheckboxes(this);

            for (int i = startIndex; i < args.Length; i++)
            {
                string arg = args[i];
                arg = arg.TrimStart('-');

                if (i + 1 < args.Length)
                {
                    string[] parts = args[i].Split('=');

                    if (parts.Length < 2)
                    {
                        Log.Write($"Missing value for argument: {arg}");
                        continue;
                    }

                    string key = parts[0].ToLower().TrimStart('-');
                    string value = parts[1];

                    // FIXME, if value is not saved, need to use default value

                    // Apply the key-value pairs to the GUI elements
                    switch (key)
                    {
                        case "input":
                            txtInputFile.Text = value;
                            break;
                        case "importformat":
                            cmbImportFormat.SelectedItem = value;
                            break;
                        case "exportformat":
                            cmbExportFormat.SelectedItem = value;
                            break;
                        case "output":
                            txtOutput.Text = value;
                            break;
                        case "offset":
                            chkAutoOffset.IsChecked = value.ToLower() == "true";
                            break;
                        case "rgb":
                            chkImportRGB.IsChecked = value.ToLower() == "true";
                            break;
                        case "intensity":
                            chkImportIntensity.IsChecked = value.ToLower() == "true";
                            break;
                        case "classification":
                            chkImportClassification.IsChecked = value.ToLower() == "true";
                            break;
                        case "gridsize":
                            txtGridSize.Text = value;
                            break;
                        case "minpoints":
                            chkUseMinPointCount.IsChecked = true;
                            txtMinPointCount.Text = value;
                            break;
                        case "scale":
                            chkUseScale.IsChecked = true;
                            txtScale.Text = value;
                            break;
                        case "swap":
                            chkSwapYZ.IsChecked = value.ToLower() == "true";
                            break;
                        case "invertx":
                            chkInvertX.IsChecked = value.ToLower() == "true";
                            break;
                        case "invertz":
                            chkInvertZ.IsChecked = value.ToLower() == "true";
                            break;
                        case "pack":
                            chkPackColors.IsChecked = value.ToLower() == "true";
                            break;
                        case "packmagic":
                            chkUsePackMagic.IsChecked = true;
                            txtPackMagic.Text = value;
                            break;
                        case "limit":
                            chkUseMaxImportPointCount.IsChecked = true;
                            txtMaxImportPointCount.Text = value;
                            break;
                        case "keep":
                            chkUseKeep.IsChecked = true;
                            txtKeepEvery.Text = value;
                            break;
                        case "maxfiles":
                            chkUseMaxFileCount.IsChecked = true;
                            txtMaxFileCount.Text = value;
                            break;
                        case "randomize":
                            chkRandomize.IsChecked = value.ToLower() == "true";
                            break;
                        case "seed":
                            chkSetRandomSeed.IsChecked = true;
                            txtRandomSeed.Text = value;
                            break;
                        case "json":
                            chkUseJSONLog.IsChecked = value.ToLower() == "true";
                            break;
                        case "metadata":
                            chkReadMetaData.IsChecked = value.ToLower() == "true";
                            break;
                        case "metadataonly":
                            chkMetaDataOnly.IsChecked = value.ToLower() == "true";
                            break;
                        case "averagetimestamp":
                            chkGetAvgTileTimestamp.IsChecked = value.ToLower() == "true";
                            break;
                        case "checkoverlap":
                            chkCalculateOverlappingTiles.IsChecked = value.ToLower() == "true";
                            break;
                        case "maxthreads":
                            txtMaxThreads.Text = value;
                            break;
                        case "customintensityrange":
                            chkCustomIntensityRange.IsChecked = value.ToLower() == "true";
                            break;
                        default:
                            Console.WriteLine($"Unknown argument: {key}");
                            break;
                    }
                }
                else
                {
                    Console.WriteLine($"Missing value for argument: {arg}");
                }
            } // for all args
        } // ImportArgs()

        private void UncheckAllCheckboxes(DependencyObject parent)
        {
            // Loop through all the child elements
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // If the child is a CheckBox, set it to unchecked
                if (child is CheckBox checkBox)
                {
                    checkBox.IsChecked = false;
                }

                // If the child is a container, recursively call the function to check its children
                if (VisualTreeHelper.GetChildrenCount(child) > 0)
                {
                    UncheckAllCheckboxes(child);
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();

            // Signal the cancellation to the worker thread
            _cancellationTokenSource.Cancel();
        }

        private void btnBrowseInput_Click(object sender, RoutedEventArgs e)
        {
            // select single file
            var dialog = new OpenFileDialog();
            dialog.Title = "Select file to import";

            if (cmbImportFormat.SelectedItem != null)
            {
                var format = cmbImportFormat.SelectedItem.ToString();
                if (format == "LAS" || format == "LAZ")
                {
                    dialog.Filter = "LAS Files|*.las;*.laz|All Files|*.*";
                }
                else if (format == "PLY")
                {
                    dialog.Filter = "PLY Files|*.ply|All Files|*.*";
                }
                else
                {
                    dialog.Filter = "All Files|*.*";
                }
            }
            else
            {
                dialog.Filter = "Point Cloud Files|*.las;*.laz;*.ply|LAS Files|*.las;*.laz|PLY Files|*.ply|All Files|*.*";
            }

            // take folder from field
            if (string.IsNullOrEmpty(txtInputFile.Text) == false)
            {
                // check if folder exists, if not take parent folder
                if (Directory.Exists(Path.GetDirectoryName(txtInputFile.Text)) == true)
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(txtInputFile.Text);
                }
                else // take parent
                {
                    var folder = Path.GetDirectoryName(txtInputFile.Text);
                    // fix slashes
                    folder = folder.Replace("\\", "/");
                    for (int i = folder.Length - 1; i > -1; i--)
                    {
                        if (folder[i] == '/')
                        {
                            if (Directory.Exists(folder.Substring(0, i)))
                            {
                                dialog.InitialDirectory = folder.Substring(0, i).Replace("/", "\\");
                                break;
                            }
                        }
                    }
                }
            }
            else // no path given
            {
                dialog.InitialDirectory = Properties.Settings.Default.lastImportFolder;
            }


            if (dialog.ShowDialog() == true)
            {
                txtInputFile.Text = dialog.FileName;
                if (string.IsNullOrEmpty(Path.GetDirectoryName(dialog.FileName)) == false) Properties.Settings.Default.lastImportFolder = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private void btnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            // TODO browse output folder

            // select single output filename
            var dialog = new SaveFileDialog();
            dialog.Title = "Set output folder and filename";
            dialog.Filter = "UCPC (V2)|*.ucpc|PCROOT (V3)|*.pcroot" + externalFileFormats;

            dialog.FilterIndex = cmbExportFormat.SelectedIndex + 1;

            // take folder from field
            if (string.IsNullOrEmpty(txtOutput.Text) == false)
            {
                // check if folder exists, if not take parent folder
                if (Directory.Exists(Path.GetDirectoryName(txtOutput.Text)) == true)
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(txtOutput.Text);
                }
                else // take parent
                {
                    var folder = Path.GetDirectoryName(txtOutput.Text);
                    // fix slashes
                    folder = folder.Replace("\\", "/");
                    for (int i = folder.Length - 1; i > -1; i--)
                    {
                        if (folder[i] == '/')
                        {
                            if (Directory.Exists(folder.Substring(0, i)))
                            {
                                dialog.InitialDirectory = folder.Substring(0, i).Replace("/", "\\");
                                break;
                            }
                        }
                    }
                }
            }
            else // no path given
            {
                dialog.InitialDirectory = Properties.Settings.Default.lastExportFolder;
            }

            if (dialog.ShowDialog() == true)
            {
                txtOutput.Text = dialog.FileName;
                if (string.IsNullOrEmpty(Path.GetDirectoryName(dialog.FileName)) == false) Properties.Settings.Default.lastExportFolder = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private void LoadSettings()
        {
            // add importer formats
            foreach (var item in Enum.GetValues(typeof(ImportFormat)))
            {
                if ((ImportFormat)item == ImportFormat.Unknown) continue;
                cmbImportFormat.Items.Add(item);
            }

            // add builtin exporter formats
            foreach (var item in Enum.GetValues(typeof(ExportFormat)))
            {
                if ((ExportFormat)item == ExportFormat.Unknown) continue;
                if ((ExportFormat)item == ExportFormat.External) continue;
                cmbExportFormat.Items.Add(item.ToString());
            }

            // Add dynamic export formats discovered from plugins
            foreach (var externalPlugin in externalWriters)
            {
                // Avoid adding duplicates if they already exist in the enum
                if (!cmbExportFormat.Items.Contains(externalPlugin.Key))
                {
                    cmbExportFormat.Items.Add(externalPlugin.Key);
                }
            }

            // TODO check if format is available in list..
            cmbImportFormat.Text = Properties.Settings.Default.importFormat;
            cmbExportFormat.Text = Properties.Settings.Default.exportFormat;

            txtInputFile.Text = Properties.Settings.Default.inputFile;
            txtOutput.Text = Properties.Settings.Default.outputFile;

            chkImportRGB.IsChecked = Properties.Settings.Default.importRGB;
            chkImportIntensity.IsChecked = Properties.Settings.Default.importIntensity;
            chkImportClassification.IsChecked = Properties.Settings.Default.importClassification;

            chkAutoOffset.IsChecked = Properties.Settings.Default.useAutoOffset;
            txtGridSize.Text = Properties.Settings.Default.gridSize.ToString();
            chkUseMinPointCount.IsChecked = Properties.Settings.Default.useMinPointCount;
            txtMinPointCount.Text = Properties.Settings.Default.minimumPointCount.ToString();
            chkUseScale.IsChecked = Properties.Settings.Default.useScale;
            txtScale.Text = Properties.Settings.Default.scale.ToString();
            chkSwapYZ.IsChecked = Properties.Settings.Default.swapYZ;
            chkInvertX.IsChecked = Properties.Settings.Default.invertX;
            chkInvertZ.IsChecked = Properties.Settings.Default.invertZ;
            chkPackColors.IsChecked = Properties.Settings.Default.packColors;
            chkUsePackMagic.IsChecked = Properties.Settings.Default.usePackMagic;
            txtPackMagic.Text = Properties.Settings.Default.packMagic.ToString();
            chkUseMaxImportPointCount.IsChecked = Properties.Settings.Default.useMaxImportPointCount;
            txtMaxImportPointCount.Text = Properties.Settings.Default.maxImportPointCount.ToString();
            chkUseSkip.IsChecked = Properties.Settings.Default.useSkip;
            txtSkipEvery.Text = Properties.Settings.Default.skipEveryN.ToString();
            chkUseKeep.IsChecked = Properties.Settings.Default.useKeep;
            txtKeepEvery.Text = Properties.Settings.Default.keepEveryN.ToString();
            chkUseMaxFileCount.IsChecked = Properties.Settings.Default.useMaxFileCount;
            txtMaxFileCount.Text = Properties.Settings.Default.maxFileCount.ToString();
            chkRandomize.IsChecked = Properties.Settings.Default.randomize;
            chkCustomIntensityRange.IsChecked = Properties.Settings.Default.customintensityrange;
            chkDetectIntensityRange.IsChecked = Properties.Settings.Default.detectIntensityRange;
            chkOpenOutputFolder.IsChecked = Properties.Settings.Default.openOutputFolder;
            chkManualOffset.IsChecked = Properties.Settings.Default.useManualOffset;
            txtOffsetX.Text = Properties.Settings.Default.manualOffsetX.ToString();
            txtOffsetY.Text = Properties.Settings.Default.manualOffsetY.ToString();
            txtOffsetZ.Text = Properties.Settings.Default.manualOffsetZ.ToString();
            chkSetRandomSeed.IsChecked = Properties.Settings.Default.useRandomSeed;
            txtRandomSeed.Text = Properties.Settings.Default.seed.ToString();
            chkUseJSONLog.IsChecked = Properties.Settings.Default.useJSON;
            chkReadMetaData.IsChecked = Properties.Settings.Default.importMetadata;
            chkMetaDataOnly.IsChecked = Properties.Settings.Default.metadataOnly;
            chkGetAvgTileTimestamp.IsChecked = Properties.Settings.Default.getAvgTileTimestamp;
            chkCalculateOverlappingTiles.IsChecked = Properties.Settings.Default.calculateOverlappingTiles;
            txtMaxThreads.Text = Properties.Settings.Default.maxThreads;
            chkUseGrid.IsChecked = Properties.Settings.Default.useGrid;
            txtOffsetMode.Text = Properties.Settings.Default.offsetMode;
            chkUseFilter.IsChecked = Properties.Settings.Default.useFilter;
            txtFilterDistance.Text = Properties.Settings.Default.filterDistance.ToString();
            chkConvertSRGB.IsChecked = Properties.Settings.Default.useSRGB;
            isInitialiazing = false;
        }

        void SaveSettings()
        {
            Properties.Settings.Default.importFormat = cmbImportFormat.Text;
            Properties.Settings.Default.exportFormat = cmbExportFormat.Text;
            Properties.Settings.Default.inputFile = txtInputFile.Text;
            Properties.Settings.Default.outputFile = txtOutput.Text;
            Properties.Settings.Default.useAutoOffset = (bool)chkAutoOffset.IsChecked;
            Properties.Settings.Default.gridSize = Tools.ParseFloat(txtGridSize.Text);
            Properties.Settings.Default.useMinPointCount = (bool)chkUseMinPointCount.IsChecked;
            Properties.Settings.Default.minimumPointCount = Tools.ParseInt(txtMinPointCount.Text);
            Properties.Settings.Default.useScale = (bool)chkUseScale.IsChecked;
            Properties.Settings.Default.scale = Tools.ParseFloat(txtScale.Text);
            Properties.Settings.Default.swapYZ = (bool)chkSwapYZ.IsChecked;
            Properties.Settings.Default.invertX = (bool)chkInvertX.IsChecked;
            Properties.Settings.Default.invertZ = (bool)chkInvertZ.IsChecked;
            Properties.Settings.Default.packColors = (bool)chkPackColors.IsChecked;
            Properties.Settings.Default.usePackMagic = (bool)chkUsePackMagic.IsChecked;
            Properties.Settings.Default.packMagic = Tools.ParseInt(txtPackMagic.Text);
            Properties.Settings.Default.useMaxImportPointCount = (bool)chkUseMaxImportPointCount.IsChecked;
            Properties.Settings.Default.maxImportPointCount = Tools.ParseInt(txtMaxImportPointCount.Text);
            Properties.Settings.Default.useSkip = (bool)chkUseSkip.IsChecked;
            Properties.Settings.Default.skipEveryN = Tools.ParseInt(txtSkipEvery.Text);
            Properties.Settings.Default.useKeep = (bool)chkUseKeep.IsChecked;
            Properties.Settings.Default.keepEveryN = Tools.ParseInt(txtKeepEvery.Text);
            Properties.Settings.Default.useMaxFileCount = (bool)chkUseMaxFileCount.IsChecked;
            Properties.Settings.Default.maxFileCount = Tools.ParseInt(txtMaxFileCount.Text);
            Properties.Settings.Default.randomize = (bool)chkRandomize.IsChecked;
            Properties.Settings.Default.customintensityrange = (bool)chkCustomIntensityRange.IsChecked;
            Properties.Settings.Default.detectIntensityRange = (bool)chkDetectIntensityRange.IsChecked;
            Properties.Settings.Default.openOutputFolder = (bool)chkOpenOutputFolder.IsChecked;
            Properties.Settings.Default.useManualOffset = (bool)chkManualOffset.IsChecked;
            float.TryParse(txtOffsetX.Text.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out float offsetX);
            Properties.Settings.Default.manualOffsetX = offsetX;
            float.TryParse(txtOffsetY.Text.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out float offsetY);
            Properties.Settings.Default.manualOffsetY = offsetY;
            float.TryParse(txtOffsetZ.Text.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out float offsetZ);
            int tempSeed = 42;
            int.TryParse(txtRandomSeed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempSeed);
            Properties.Settings.Default.seed = tempSeed;
            Properties.Settings.Default.useJSON = (bool)chkUseJSONLog.IsChecked;
            Properties.Settings.Default.importMetadata = (bool)chkReadMetaData.IsChecked;
            Properties.Settings.Default.useRandomSeed = (bool)chkSetRandomSeed.IsChecked;
            Properties.Settings.Default.manualOffsetZ = offsetZ;
            Properties.Settings.Default.metadataOnly = (bool)chkMetaDataOnly.IsChecked;
            Properties.Settings.Default.getAvgTileTimestamp = (bool)chkGetAvgTileTimestamp.IsChecked;
            Properties.Settings.Default.calculateOverlappingTiles = (bool)chkCalculateOverlappingTiles.IsChecked;
            Properties.Settings.Default.maxThreads = txtMaxThreads.Text;
            Properties.Settings.Default.useGrid = (bool)chkUseGrid.IsChecked;
            Properties.Settings.Default.offsetMode = txtOffsetMode.Text;
            Properties.Settings.Default.useFilter = (bool)chkUseFilter.IsChecked;
            Properties.Settings.Default.filterDistance = Tools.ParseFloat(txtFilterDistance.Text);
            Properties.Settings.Default.useSRGB = (bool)chkConvertSRGB.IsChecked;
            Properties.Settings.Default.Save();
        }

        private void btnGetParams_Click(object sender, RoutedEventArgs e)
        {
            StartProcess(false);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Log.Write("Aborting - Please wait..");
            _cancellationTokenSource.Cancel();
        }

        private void cmbExportFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitialiazing == true) return;

            // get current output path
            var currentOutput = txtOutput.Text;

            // check if output is file or directory
            if (Directory.Exists(currentOutput) == true) // its directory
            {
                // if PCROOT then filename is required, use default output.pcroot then
                if (cmbExportFormat.SelectedValue.ToString().ToUpper().Contains("PCROOT"))
                {
                    string sourceName = Path.GetFileNameWithoutExtension(txtInputFile.Text);
                    if (string.IsNullOrEmpty(sourceName)) sourceName = "output";

                    txtOutput.Text = Path.Combine(currentOutput, sourceName + ".pcroot");
                }
                return;
            }

            // check if file has extension already
            if (string.IsNullOrEmpty(Path.GetExtension(currentOutput)) == false)
            {
                // add extension based on selected format
                txtOutput.Text = Path.ChangeExtension(currentOutput, "." + cmbExportFormat.SelectedValue.ToString().ToLower());
            }
            else // no extension, set default filename
            {
                // check if have filename
                if (string.IsNullOrEmpty(Path.GetFileName(currentOutput)) == false)
                {
                    // add extension based on selected format
                    txtOutput.Text = Path.Combine(Path.GetDirectoryName(currentOutput), Path.GetFileName(currentOutput) + "." + cmbExportFormat.SelectedValue.ToString().ToLower());
                }
                else // no filename, set default
                {
                    //Log.Write("cmbExportFormat.SelectedValue> " + cmbExportFormat.SelectedValue.ToString());
                    if (string.IsNullOrEmpty(currentOutput)) return;
                    txtOutput.Text = Path.Combine(Path.GetDirectoryName(currentOutput), "output." + cmbExportFormat.SelectedValue.ToString().ToLower());
                }
            }
        }

        private void chkImportRGB_Checked(object sender, RoutedEventArgs e)
        {
            // not available at init
            if (isInitialiazing == true) return;

            //chkImportIntensity.IsChecked = false;
            Properties.Settings.Default.importRGB = true;
            Properties.Settings.Default.Save();
        }

        private void chkImportRGB_Unchecked(object sender, RoutedEventArgs e)
        {
            if (isInitialiazing == true) return;
            Properties.Settings.Default.importRGB = false;

            //chkImportIntensity.IsChecked = true;
            //Properties.Settings.Default.importIntensity = true;
            Properties.Settings.Default.Save();
        }

        private void chkImportIntensity_Checked(object sender, RoutedEventArgs e)
        {
            if (isInitialiazing == true) return;

            //chkImportRGB.IsChecked = false;
            Properties.Settings.Default.importIntensity = true;
            Properties.Settings.Default.Save();
        }

        private void chkImportIntensity_Unchecked(object sender, RoutedEventArgs e)
        {
            if (isInitialiazing == true) return;
            Properties.Settings.Default.importIntensity = false;

            //chkImportRGB.IsChecked = true;
            //Properties.Settings.Default.importRGB = true;
            Properties.Settings.Default.importIntensity = false;
            Properties.Settings.Default.Save();
        }

        private void chkImportClassification_Checked(object sender, RoutedEventArgs e)
        {
            if (isInitialiazing == true) return;

            // TODO for now only can import classification as RGB color
            Properties.Settings.Default.importClassification = true;
            Properties.Settings.Default.Save();
        }

        private void chkImportClassification_Unchecked(object sender, RoutedEventArgs e)
        {
            if (isInitialiazing == true) return;
            // TODO for now only can import classification as RGB color
            Properties.Settings.Default.importClassification = false;
            Properties.Settings.Default.Save();
        }


        private void txtInputFile_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void txtInputFile_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    txtInputFile.Text = files[0];
                }
            }
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "https://github.com/unitycoder/PointCloudConverter/wiki",
                    UseShellExecute = true
                };
                Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open the link. Error: {ex.Message}");
            }
        }

        private void chkAutoOffset_Checked(object sender, RoutedEventArgs e)
        {
            if (isInitialiazing == true) return;

            if (chkAutoOffset.IsChecked == true && chkManualOffset.IsChecked == true)
            {
                chkManualOffset.IsChecked = false;
            }
        }

        private void chkManualOffset_Checked(object sender, RoutedEventArgs e)
        {
            if (isInitialiazing == true) return;

            if (chkManualOffset.IsChecked == true && chkAutoOffset.IsChecked == true)
            {
                chkAutoOffset.IsChecked = false;
            }
        }

        private void btnCopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtConsole.Text);
            txtConsole.Focus();
            txtConsole.SelectAll();
            e.Handled = true;
        }

        private void btnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Title = "Import settings file";
            dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";

            // if have previously used config dir use that, if not, use local config dir if exists, if neither, use default..
            if (string.IsNullOrEmpty(Properties.Settings.Default.lastUsedConfigFolder) == false)
            {
                dialog.InitialDirectory = Properties.Settings.Default.lastUsedConfigFolder;
            }
            else if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "configs")))
            {
                dialog.InitialDirectory = Path.Combine(Directory.GetCurrentDirectory(), "configs");
            }

            if (dialog.ShowDialog() == true)
            {
                if (File.Exists(dialog.FileName))
                {
                    var contents = File.ReadAllText(dialog.FileName);
                    ImportArgs(contents);
                    Properties.Settings.Default.lastUsedConfigFolder = Path.GetDirectoryName(dialog.FileName);
                }
            }
        }

        private void btnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog();
            dialog.Title = "Save settings file";
            dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";

            if (string.IsNullOrEmpty(Properties.Settings.Default.lastUsedConfigFolder) == false)
            {
                dialog.InitialDirectory = Properties.Settings.Default.lastUsedConfigFolder;
            }
            else if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "configs")))
            {
                dialog.InitialDirectory = Path.Combine(Directory.GetCurrentDirectory(), "configs");
            }

            if (dialog.ShowDialog() == true)
            {
                StartProcess(false);
                File.WriteAllText(dialog.FileName, txtConsole.Text);
                Properties.Settings.Default.lastUsedConfigFolder = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private void btnPlugins_Click(object sender, RoutedEventArgs e)
        {
            // open plugins folder in explorer at location of this exe
            var pluginsFolder = Path.Combine(Directory.GetCurrentDirectory(), "plugins");
            if (Directory.Exists(pluginsFolder) == false)
            {
                Directory.CreateDirectory(pluginsFolder);
            }
            Process.Start(new ProcessStartInfo("explorer.exe", pluginsFolder));
        }

    } // class
} // namespace
