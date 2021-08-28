// standalone point cloud converter https://github.com/unitycoder/PointCloudConverter

using Microsoft.Win32;
using PointCloudConverter.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace PointCloudConverter
{
    public partial class MainWindow : Window
    {
        static string appname = "PointCloud Converter v1.74";
        static readonly string rootFolder = AppDomain.CurrentDomain.BaseDirectory;

        // allow console output from WPF application https://stackoverflow.com/a/7559336/5452781
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);
        const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;

        // detach from console, otherwise file is locked https://stackoverflow.com/a/29572349/5452781
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeConsole();

        public MainWindow()
        {
            InitializeComponent();
            Main();
        }

        private void Main()
        {
            // check cmdline args
            string[] args = Environment.GetCommandLineArgs();

            Tools.FixDLLFoldersAndConfig(rootFolder);
            Tools.ForceDotCultureSeparator();

            if (args.Length > 1)
            {
                AttachConsole(ATTACH_PARENT_PROCESS);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n::: " + appname + " :::\n");
                Console.ForegroundColor = ConsoleColor.White;

                // check args
                var importSettings = ArgParser.Parse(args, rootFolder);

                // if have files, process them
                if (importSettings != null) ProcessAllFiles(importSettings);

                // end output
                Console.WriteLine("Exit");

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
        private static void ProcessAllFiles(ImportSettings importSettings)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // if user has set maxFiles param, loop only that many files
            importSettings.maxFiles = importSettings.maxFiles > 0 ? importSettings.maxFiles : importSettings.inputFiles.Count;
            importSettings.maxFiles = Math.Min(importSettings.maxFiles, importSettings.inputFiles.Count);

            // loop input files
            for (int i = 0, len = importSettings.maxFiles; i < len; i++)
            {
                Console.WriteLine("\nReading file (" + i + "/" + (len - 1) + ") : " + importSettings.inputFiles[i] + " (" + Tools.HumanReadableFileSize(new FileInfo(importSettings.inputFiles[i]).Length) + ")");

                // do actual point cloud parsing for this file
                ParseFile(importSettings, i);
            }

            stopwatch.Stop();
            Console.WriteLine("Elapsed: " + (TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds)).ToString(@"hh\h\ mm\m\ ss\s\ ms\m\s"));
            stopwatch.Reset();
        }

        // process single file
        static void ParseFile(ImportSettings importSettings, int fileIndex)
        {
            var res = importSettings.reader.InitReader(importSettings.inputFiles[fileIndex]);
            if (res == false)
            {
                Console.WriteLine("Unknown error while initializing reader: " + importSettings.inputFiles[fileIndex]);
                return;
            }

            // NOTE pointcount not available in all formats
            int fullPointCount = importSettings.reader.GetPointCount();
            int pointCount = fullPointCount;

            // show stats for decimations
            if (importSettings.skipPoints == true)
            {
                var afterSkip = (int)Math.Floor(pointCount - (pointCount / (float)importSettings.skipEveryN));
                Console.WriteLine("Skip every X points is enabled, original points: " + fullPointCount + ", After skipping:" + afterSkip);
            }

            if (importSettings.keepPoints == true)
            {
                Console.WriteLine("Keep every x points is enabled, original points: " + fullPointCount + ", After keeping:" + (pointCount / importSettings.keepEveryN));
            }

            if (importSettings.useLimit == true)
            {
                Console.WriteLine("Original points: " + pointCount + " Limited points: " + importSettings.limit);
                pointCount = importSettings.limit > pointCount ? pointCount : importSettings.limit;
            }
            else
            {
                Console.WriteLine("Points: " + pointCount);
            }

            // NOTE only works with formats that have bounds defined in header, otherwise need to loop whole file to get bounds
            var bounds = importSettings.reader.GetBounds();

            if (importSettings.useAutoOffset == true)
            {
                // get offset only from the first file, other files use same offset
                if (fileIndex == 0)
                {
                    // offset cloud to be near 0,0,0
                    importSettings.offsetX = -bounds.minX;
                    importSettings.offsetY = -bounds.minY;
                    importSettings.offsetZ = -bounds.minZ;
                }
            }

            var writerRes = importSettings.writer.InitWriter(importSettings, pointCount);
            if (writerRes == false)
            {
                Console.WriteLine("Error> Failed to initialize Writer");
                return;
            }

            // Loop all points
            for (int i = 0; i < fullPointCount; i++)
            {
                // stop at limit count
                if (importSettings.useLimit == true && i > pointCount) break;

                // get point XYZ
                Float3 point = importSettings.reader.GetXYZ();
                if (point.hasError == true) break;

                // add offset if enabled
                point.x = importSettings.useAutoOffset ? point.x + importSettings.offsetX : point.x;
                point.y = importSettings.useAutoOffset ? point.y + importSettings.offsetY : point.y;
                point.z = importSettings.useAutoOffset ? point.z + importSettings.offsetZ : point.z;

                // scale if enabled
                point.x = importSettings.useScale ? point.x * importSettings.scale : point.x;
                point.y = importSettings.useScale ? point.y * importSettings.scale : point.y;
                point.z = importSettings.useScale ? point.z * importSettings.scale : point.z;

                // flip if enabled
                if (importSettings.swapYZ == true)
                {
                    var tempZ = point.z;
                    point.z = point.y;
                    point.y = tempZ;
                }

                // get point color
                Color rgb = importSettings.reader.GetRGB();

                // collect this point XYZ and RGB into node
                importSettings.writer.AddPoint(i, (float)point.x, (float)point.y, (float)point.z, rgb.r, rgb.g, rgb.b);
            }

            importSettings.writer.Save(fileIndex);
            importSettings.reader.Close();

            // if this was last file
            if (fileIndex == (importSettings.maxFiles - 1))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Finished!");
                Console.ForegroundColor = ConsoleColor.White;
            }
        } // ParseFile

        private void btnConvert_Click(object sender, RoutedEventArgs e)
        {
            StartProcess();
        }

        void StartProcess(bool skipProcess = true)
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
            if ((bool)chkAutoOffset.IsChecked) args.Add("-offset=" + (bool)chkAutoOffset.IsChecked);

            if (cmbExportFormat.SelectedItem.ToString().ToUpper().Contains("PCROOT")) args.Add("-gridsize=" + txtGridSize.Text);

            if ((bool)chkUseMinPointCount.IsChecked) args.Add("-minpoints=" + txtMinPointCount.Text);
            if ((bool)chkUseScale.IsChecked) args.Add("-scale=" + txtScale.Text);
            if ((bool)chkSwapYZ.IsChecked) args.Add("-swap=" + (bool)chkSwapYZ.IsChecked);
            if ((bool)chkPackColors.IsChecked) args.Add("-pack=" + (bool)chkPackColors.IsChecked);
            if ((bool)chkUsePackMagic.IsChecked) args.Add("-packmagic=" + txtPackMagic.Text);
            if ((bool)chkUseMaxImportPointCount.IsChecked) args.Add("-limit=" + txtMaxImportPointCount.Text);
            if ((bool)chkUseSkip.IsChecked) args.Add("-skip=" + txtSkipEvery.Text);
            if ((bool)chkUseKeep.IsChecked) args.Add("-keep=" + txtKeepEvery.Text);
            if ((bool)chkUseMaxFileCount.IsChecked) args.Add("-maxfiles=" + txtMaxFileCount.Text);
            args.Add("-randomize=" + (bool)chkRandomize.IsChecked);

            // check input files
            var importSettings = ArgParser.Parse(args.ToArray(), rootFolder);
            // TODO get error messages into log textbox (return in settings?)

            // if have files, process them
            if (importSettings != null)
            {
                // show output settings for commandline
                var cl = string.Join(" ", args);
                txtConsole.Text = cl;
                Console.WriteLine(cl);

                // TODO lock UI, add cancel button, add progress bar
                if (skipProcess == true) ProcessAllFiles(importSettings);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();
        }

        private void btnBrowseInput_Click(object sender, RoutedEventArgs e)
        {
            // TODO browse for folder, not file

            // select single file
            var dialog = new OpenFileDialog();
            dialog.Title = "Select file to import";
            dialog.Filter = "LAS|*.las;*.laz";
            dialog.InitialDirectory = Properties.Settings.Default.lastImportFolder;
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
            dialog.InitialDirectory = Properties.Settings.Default.lastExportFolder;
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
            chkAutoOffset.IsChecked = Properties.Settings.Default.useAutoOffset;
            txtGridSize.Text = Properties.Settings.Default.gridSize.ToString();
            chkUseMinPointCount.IsChecked = Properties.Settings.Default.useMinPointCount;
            txtMinPointCount.Text = Properties.Settings.Default.minimumPointCount.ToString();
            chkUseScale.IsChecked = Properties.Settings.Default.useScale;
            txtScale.Text = Properties.Settings.Default.scale.ToString();
            chkSwapYZ.IsChecked = Properties.Settings.Default.swapYZ;
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
            Properties.Settings.Default.Save();
        }

        private void btnGetParams_Click(object sender, RoutedEventArgs e)
        {
            StartProcess(false);
        }
    } // class
} // namespace
