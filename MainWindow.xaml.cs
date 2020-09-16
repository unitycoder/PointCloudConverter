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
        static string appname = "PointCloud Converter v1.72";
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

            if (args.Length > 2)
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

            // TODO loadsettings

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
                if (importSettings.flipYZ == true)
                {
                    var tempZ = point.z;
                    point.z = point.y;
                    point.y = tempZ;
                }

                // get point color
                Color rgb = importSettings.reader.GetRGB();

                // collect this point XYZ and RGB
                importSettings.writer.AddPoint(i, point.x, point.y, point.z, rgb.r, rgb.g, rgb.b);
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
            // get fake args from GUI settings
            var args = new List<string>();

            // TODO add enabled args to list

            // check input files
            var importSettings = ArgParser.Parse(args.ToArray(), rootFolder);
            // TODO get error messages into log textbox (return in settings?)

            // if have files, process them
            if (importSettings != null) ProcessAllFiles(importSettings);

        }



        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // TODO save settings
        }

        private void btnBrowseInput_Click(object sender, RoutedEventArgs e)
        {
            // TODO browse for single, multiple files or folder
        }

        private void btnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            // TODO browse output filename or folder
        }

    } // class
} // namespace
