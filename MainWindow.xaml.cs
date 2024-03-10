// Standalone Point Cloud Converter https://github.com/unitycoder/PointCloudConverter

using PointCloudConverter.Logger;
using PointCloudConverter.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = PointCloudConverter.Structs.Color;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace PointCloudConverter
{
    public partial class MainWindow : Window
    {
        static readonly string version = "09.03.2024";
        static readonly string appname = "PointCloud Converter - " + version;
        static readonly string rootFolder = AppDomain.CurrentDomain.BaseDirectory;

        // allow console output from WPF application https://stackoverflow.com/a/7559336/5452781
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);
        const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;

        // detach from console, otherwise file is locked https://stackoverflow.com/a/29572349/5452781
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeConsole();

        Thread workerThread;
        static bool abort = false;
        public static MainWindow mainWindowStatic;
        bool isInitialiazing = true;

        public MainWindow()
        {
            InitializeComponent();
            mainWindowStatic = this;
            Main();
        }

        private readonly ILogger logger;

        private void Main()
        {
            // check cmdline args
            string[] args = Environment.GetCommandLineArgs();

            Tools.FixDLLFoldersAndConfig(rootFolder);
            Tools.ForceDotCultureSeparator();

            // default logger
            Log.CreateLogger(isJSON: false, version: version);

            if (args.Length > 1)
            {
                AttachConsole(ATTACH_PARENT_PROCESS);

                // check if have -jsonlog=true
                foreach (var arg in args)
                {
                    if (arg.ToLower().Contains("-json=true"))
                    {
                        Log.CreateLogger(isJSON: true, version: version);
                    }
                }


                Console.ForegroundColor = ConsoleColor.Cyan;
                Log.WriteLine("\n::: " + appname + " :::\n");
                //Console.WriteLine("\n::: " + appname + " :::\n");
                Console.ForegroundColor = ConsoleColor.White;

                // check args, null here because we get the args later
                var importSettings = ArgParser.Parse(null, rootFolder);

                if (importSettings.useJSONLog)
                {
                    importSettings.version = version;
                    Log.SetSettings(importSettings);
                }

                //if (importSettings.useJSONLog) log.Init(importSettings, version);

                // get elapsed time using time
                var startTime = DateTime.Now;


                // if have files, process them
                if (importSettings.errors.Count == 0)
                {
                    // NOTE no background thread from commandline
                    ProcessAllFiles(importSettings);
                }

                // print time
                var endTime = DateTime.Now;
                var elapsed = endTime - startTime;
                string elapsedString = elapsed.ToString(@"hh\h\ mm\m\ ss\s\ ms\m\s");

                // end output
                Log.WriteLine("Exited.\nElapsed: " + elapsedString);
                if (importSettings.useJSONLog)
                {
                    Log.WriteLine("{\"event\": \"" + LogEvent.End + "\", \"elapsed\": \"" + elapsedString + "\",\"version\":\"" + version + "\"}", LogEvent.End);
                }
                // hack for console exit https://stackoverflow.com/a/67940480/5452781
                SendKeys.SendWait("{ENTER}");
                FreeConsole();
                Environment.Exit(0);
            }

            // regular WPF starts from here
            this.Title = appname;

            // disable accesskeys without alt
            CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope = true;

            LoadSettings();
        }

        // main processing loop
        private static void ProcessAllFiles(System.Object importSettingsObject)
        {
            var importSettings = (ImportSettings)importSettingsObject;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // if user has set maxFiles param, loop only that many files
            importSettings.maxFiles = importSettings.maxFiles > 0 ? importSettings.maxFiles : importSettings.inputFiles.Count;
            importSettings.maxFiles = Math.Min(importSettings.maxFiles, importSettings.inputFiles.Count);

            StartProgressTimer();

            // loop input files
            progressFile = 0;
            progressTotalFiles = importSettings.maxFiles - 1;
            if (progressTotalFiles < 0) progressTotalFiles = 0;

            List<Float3> boundsListTemp = new List<Float3>();

            // get all file bounds, if in batch mode and RGB+INT+PACK
            // TODO: check what happens if its too high? over 128/256?
            if (importSettings.useAutoOffset == true && importSettings.importIntensity == true && importSettings.importRGB == true && importSettings.packColors == true)
            {
                for (int i = 0, len = importSettings.maxFiles; i < len; i++)
                {
                    progressFile = i;
                    Log.WriteLine("\nReading bounds from file (" + (i + 1) + "/" + len + ") : " + importSettings.inputFiles[i] + " (" + Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[i]).Length) + ")");
                    var res = GetBounds(importSettings, i);

                    if (res.Item1 == true)
                    {
                        boundsListTemp.Add(new Float3(res.Item2, res.Item3, res.Item4));
                    }
                }

                // print lowest bounds from boundsListTemp
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
                // TODO could take center for XZ, and lowest for Y
                importSettings.offsetX = lowestX + 0.1f;
                importSettings.offsetY = lowestY + 0.1f;
                importSettings.offsetZ = lowestZ + 0.1f;
            }

            progressFile = 0;
            for (int i = 0, len = importSettings.maxFiles; i < len; i++)
            {
                progressFile = i;
                Log.WriteLine("\nReading file (" + (i + 1) + "/" + len + ") : " + importSettings.inputFiles[i] + " (" + Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[i]).Length) + ")");
                //Debug.WriteLine("\nReading file (" + (i + 1) + "/" + len + ") : " + importSettings.inputFiles[i] + " (" + Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[i]).Length) + ")");
                //if (abort==true) 
                // do actual point cloud parsing for this file
                ParseFile(importSettings, i);
            }

            stopwatch.Stop();
            Log.WriteLine("Elapsed: " + (TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds)).ToString(@"hh\h\ mm\m\ ss\s\ ms\m\s"));
            stopwatch.Reset();

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                mainWindowStatic.HideProcessingPanel();
                // call update one more time
                ProgressTick(null, null);
                // clear timer
                progressTimerThread.Stop();
                mainWindowStatic.progressBarFiles.Foreground = Brushes.Green;
                mainWindowStatic.progressBarPoints.Foreground = Brushes.Green;
            }));
        }

        void HideProcessingPanel()
        {
            gridProcessingPanel.Visibility = Visibility.Hidden;
        }

        // progress bar data
        static int progressPoint = 0;
        static int progressTotalPoints = 0;
        static int progressFile = 0;
        static int progressTotalFiles = 0;
        static DispatcherTimer progressTimerThread;
        public static string lastStatusMessage = "";

        static void StartProgressTimer()
        {
            progressTimerThread = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher);
            progressTimerThread.Tick += ProgressTick;
            progressTimerThread.Interval = TimeSpan.FromSeconds(1);
            progressTimerThread.Start();

            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                mainWindowStatic.progressBarFiles.Foreground = Brushes.Red;
                mainWindowStatic.progressBarPoints.Foreground = Brushes.Red;
                mainWindowStatic.lblStatus.Content = "";
            }));
        }

        static void ProgressTick(object sender, EventArgs e)
        {
            if (progressTotalPoints > 0)
            {
                mainWindowStatic.progressBarFiles.Value = progressFile / (float)(progressTotalFiles + 1);
                mainWindowStatic.progressBarPoints.Value = progressPoint / (float)progressTotalPoints;
                mainWindowStatic.lblStatus.Content = lastStatusMessage;
            }
            else
            {
                mainWindowStatic.progressBarFiles.Value = 0;
                mainWindowStatic.progressBarPoints.Value = 0;
                mainWindowStatic.lblStatus.Content = "";
            }
        }

        static (bool, float, float, float) GetBounds(ImportSettings importSettings, int fileIndex)
        {
            var res = importSettings.reader.InitReader(importSettings, fileIndex);
            if (res == false)
            {
                Log.WriteLine("Unknown error while initializing reader: " + importSettings.inputFiles[fileIndex]);
                return (false, 0, 0, 0);
            }
            var bounds = importSettings.reader.GetBounds();
            //Console.WriteLine(bounds.minX + " " + bounds.minY + " " + bounds.minZ);

            importSettings.reader.Close();

            return (true, bounds.minX, bounds.minY, bounds.minZ);
        }

        // process single file
        static void ParseFile(ImportSettings importSettings, int fileIndex)
        {
            var res = importSettings.reader.InitReader(importSettings, fileIndex);
            if (res == false)
            {
                Log.WriteLine("Unknown error while initializing reader: " + importSettings.inputFiles[fileIndex]);
                return;
            }

            // NOTE pointcount not available in all formats
            int fullPointCount = importSettings.reader.GetPointCount();
            int pointCount = fullPointCount;

            // show stats for decimations
            if (importSettings.skipPoints == true)
            {
                var afterSkip = (int)Math.Floor(pointCount - (pointCount / (float)importSettings.skipEveryN));
                Log.WriteLine("Skip every X points is enabled, original points: " + fullPointCount + ", After skipping:" + afterSkip);
                pointCount = afterSkip;
            }

            if (importSettings.keepPoints == true)
            {
                Log.WriteLine("Keep every x points is enabled, original points: " + fullPointCount + ", After keeping:" + (pointCount / importSettings.keepEveryN));
                pointCount = pointCount / importSettings.keepEveryN;
            }

            if (importSettings.useLimit == true)
            {
                Log.WriteLine("Original points: " + pointCount + " Limited points: " + importSettings.limit);
                pointCount = importSettings.limit > pointCount ? pointCount : importSettings.limit;
            }
            else
            {
                Log.WriteLine("Points: " + pointCount);
            }

            // NOTE only works with formats that have bounds defined in header, otherwise need to loop whole file to get bounds?

            // dont use these bounds, in this case
            if (importSettings.useAutoOffset == true && importSettings.importIntensity == true && importSettings.importRGB == true && importSettings.packColors == true)
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

            var writerRes = importSettings.writer.InitWriter(importSettings, pointCount);
            if (writerRes == false)
            {
                Log.WriteLine("Error> Failed to initialize Writer");
                return;
            }

            progressPoint = 0;
            progressTotalPoints = importSettings.useLimit ? pointCount : fullPointCount;

            lastStatusMessage = "Processing points..";

            string jsonString = "{" +
            "\"event\": \"" + LogEvent.File + "\"," +
            "\"path\": " + JsonSerializer.Serialize(importSettings.inputFiles[fileIndex]) + "," +
            "\"size\": " + new FileInfo(importSettings.inputFiles[fileIndex]).Length + "," +
            "\"points\": " + pointCount + "," +
            "\"status\": \"" + LogStatus.Processing + "\"" +
            "}";

            Log.WriteLine(jsonString, LogEvent.File);

            // Loop all points
            for (int i = 0; i < fullPointCount; i++)
            {
                // stop at limit count
                if (importSettings.useLimit == true && i > pointCount) break;

                // get point XYZ
                Float3 point = importSettings.reader.GetXYZ();
                if (point.hasError == true) break;

                // add offsets (its 0 if not used)
                point.x -= importSettings.offsetX;
                point.y -= importSettings.offsetY;
                point.z -= importSettings.offsetZ;

                // scale if enabled
                point.x = importSettings.useScale ? point.x * importSettings.scale : point.x;
                point.y = importSettings.useScale ? point.y * importSettings.scale : point.y;
                point.z = importSettings.useScale ? point.z * importSettings.scale : point.z;

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

                if (importSettings.importRGB == true)
                {
                    rgb = importSettings.reader.GetRGB();
                }

                // TODO get intensity as separate value, TODO is this float or rgb?
                if (importSettings.importIntensity == true)
                {
                    intensity = importSettings.reader.GetIntensity();
                    //if (i < 100) Console.WriteLine(intensity.r);

                    // if no rgb, then replace RGB with intensity
                    if (importSettings.importRGB == false)
                    {
                        rgb.r = intensity.r;
                        rgb.g = intensity.r;
                        rgb.b = intensity.r;
                    }
                }

                // collect this point XYZ and RGB into node, optionally intensity also
                importSettings.writer.AddPoint(i, (float)point.x, (float)point.y, (float)point.z, rgb.r, rgb.g, rgb.b, importSettings.importIntensity, intensity.r);
                progressPoint = i;
            }

            lastStatusMessage = "Saving files..";
            importSettings.writer.Save(fileIndex);
            lastStatusMessage = "Finished saving..";
            importSettings.reader.Close();

            // if this was last file
            if (fileIndex == (importSettings.maxFiles - 1))
            {
                lastStatusMessage = "Done!";
                Console.ForegroundColor = ConsoleColor.Green;
                Log.WriteLine("Finished!");
                Console.ForegroundColor = ConsoleColor.White;
                mainWindowStatic.Dispatcher.Invoke(() =>
                {
                    if ((bool)mainWindowStatic.chkOpenOutputFolder.IsChecked)
                    {
                        var dir = Path.GetDirectoryName(importSettings.outputFile);
                        if (Directory.Exists(dir))
                        {
                            Process.Start(dir);
                        }
                    }
                });
            }
        } // ParseFile

        private void btnConvert_Click(object sender, RoutedEventArgs e)
        {
            // reset progress
            progressTotalFiles = 0;
            progressTotalPoints = 0;
            ProgressTick(null, null);

            gridProcessingPanel.Visibility = Visibility.Visible;
            SaveSettings();
            StartProcess();
        }

        void StartProcess(bool doProcess = true)
        {
            // get args from GUI settings, TODO could directly create new import settings..
            var args = new List<string>();

            // add enabled args to list, TODO use binding later?
            args.Add("-input=" + txtInputFile.Text);

            if (cmbImportFormat.SelectedItem != null)
            {
                args.Add("-importformat=" + cmbImportFormat.SelectedItem.ToString());
            }
            if (cmbExportFormat.SelectedItem != null)
            {
                args.Add("-exportformat=" + cmbExportFormat.SelectedItem.ToString());
            }
            args.Add("-output=" + txtOutput.Text);

            args.Add("-offset=" + (bool)chkAutoOffset.IsChecked);
            args.Add("-rgb=" + (bool)chkImportRGB.IsChecked);
            args.Add("-intensity=" + (bool)chkImportIntensity.IsChecked);

            if (cmbExportFormat.SelectedItem.ToString().ToUpper().Contains("PCROOT")) args.Add("-gridsize=" + txtGridSize.Text);

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
            if ((bool)chkManualOffset.IsChecked) args.Add("-offset=" + txtOffsetX.Text + "," + txtOffsetY.Text + "," + txtOffsetZ.Text);
            args.Add("-randomize=" + (bool)chkRandomize.IsChecked);
            if ((bool)chkSetRandomSeed.IsChecked) args.Add("-seed=" + txtRandomSeed.Text);
            if ((bool)chkUseJSONLog.IsChecked) args.Add("-json=true");

            if (((bool)chkImportIntensity.IsChecked) && ((bool)chkCustomIntensityRange.IsChecked)) args.Add("-customintensityrange=True");

            // check input files
            var importSettings = ArgParser.Parse(args.ToArray(), rootFolder);

            // if have files, process them
            if (importSettings.errors.Count == 0)
            {
                // show output settings for commandline
                var cl = string.Join(" ", args);
                txtConsole.Text = cl;
                Console.WriteLine(cl);

                // TODO lock UI, add cancel button, add progress bar
                if (doProcess == true)
                {
                    ParameterizedThreadStart start = new ParameterizedThreadStart(ProcessAllFiles);
                    workerThread = new Thread(start);
                    workerThread.IsBackground = true;
                    workerThread.Start(importSettings);
                }
            }
            else
            {
                HideProcessingPanel();
                txtConsole.Text = "Operation failed! " + string.Join(Environment.NewLine, importSettings.errors);
            }
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();

            abort = true;
            if (workerThread != null)
            {
                workerThread.Abort();
                Environment.Exit(Environment.ExitCode);
            }
        }

        private void btnBrowseInput_Click(object sender, RoutedEventArgs e)
        {
            // select single file
            var dialog = new OpenFileDialog();
            dialog.Title = "Select file to import";
            dialog.Filter = "LAS|*.las;*.laz";

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
            dialog.Filter = "UCPC (V2)|*.ucpc|PCROOT (V3)|*.pcroot";

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
            foreach (var item in Enum.GetValues(typeof(ImportFormat)))
            {
                if ((ImportFormat)item == ImportFormat.Unknown) continue;
                cmbImportFormat.Items.Add(item);
            }

            foreach (var item in Enum.GetValues(typeof(ExportFormat)))
            {
                if ((ExportFormat)item == ExportFormat.Unknown) continue;
                cmbExportFormat.Items.Add(item);
            }

            // TODO check if format is available in list..
            cmbImportFormat.Text = Properties.Settings.Default.importFormat;
            cmbExportFormat.Text = Properties.Settings.Default.exportFormat;

            txtInputFile.Text = Properties.Settings.Default.inputFile;
            txtOutput.Text = Properties.Settings.Default.outputFile;

            chkImportRGB.IsChecked = Properties.Settings.Default.importRGB;
            chkImportIntensity.IsChecked = Properties.Settings.Default.importIntensity;

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
            chkOpenOutputFolder.IsChecked = Properties.Settings.Default.openOutputFolder;
            chkManualOffset.IsChecked = Properties.Settings.Default.useManualOffset;
            txtOffsetX.Text = Properties.Settings.Default.manualOffsetX.ToString();
            txtOffsetY.Text = Properties.Settings.Default.manualOffsetY.ToString();
            txtOffsetZ.Text = Properties.Settings.Default.manualOffsetZ.ToString();
            chkSetRandomSeed.IsChecked = Properties.Settings.Default.useRandomSeed;
            txtRandomSeed.Text = Properties.Settings.Default.seed.ToString();
            chkUseJSONLog.IsChecked = Properties.Settings.Default.useJSON;
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
            Properties.Settings.Default.openOutputFolder = (bool)chkOpenOutputFolder.IsChecked;
            Properties.Settings.Default.useManualOffset = (bool)chkManualOffset.IsChecked;
            float.TryParse(txtOffsetX.Text, out float offsetX);
            Properties.Settings.Default.manualOffsetX = offsetX;
            float.TryParse(txtOffsetY.Text, out float offsetY);
            Properties.Settings.Default.manualOffsetY = offsetY;
            float.TryParse(txtOffsetZ.Text, out float offsetZ);
            int tempSeed = 42;
            int.TryParse(txtRandomSeed.Text, out tempSeed);
            Properties.Settings.Default.seed = tempSeed;
            Properties.Settings.Default.useJSON = (bool)chkUseJSONLog.IsChecked;
            Properties.Settings.Default.useRandomSeed = (bool)chkSetRandomSeed.IsChecked;
            Properties.Settings.Default.manualOffsetZ = offsetZ; Properties.Settings.Default.Save();
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
            abort = true;
            if (workerThread != null)
            {
                workerThread.Abort();
                Environment.Exit(Environment.ExitCode);
            }
        }

        private void cmbExportFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // updatae file extension, if set
            txtOutput.Text = Path.ChangeExtension(txtOutput.Text, "." + cmbExportFormat.SelectedValue.ToString().ToLower());
        }

        private void chkImportRGB_Checked(object sender, RoutedEventArgs e)
        {
            // not available at init
            if (isInitialiazing == true) return;

            //chkImportIntensity.IsChecked = false;
            Properties.Settings.Default.importRGB = true;
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

            chkImportRGB.IsChecked = true;
            Properties.Settings.Default.importRGB = true;
            Properties.Settings.Default.Save();
        }

        private void chkImportRGB_Unchecked(object sender, RoutedEventArgs e)
        {
            if (isInitialiazing == true) return;
            Properties.Settings.Default.importRGB = false;

            chkImportIntensity.IsChecked = true;
            Properties.Settings.Default.importIntensity = true;
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
            Process.Start("https://github.com/unitycoder/PointCloudConverter/wiki");
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
            // copy console to clipboard
            System.Windows.Clipboard.SetText(txtConsole.Text);
            // focus
            txtConsole.Focus();
            // select all text
            txtConsole.SelectAll();
            e.Handled = true;
        }
    } // class
} // namespace
