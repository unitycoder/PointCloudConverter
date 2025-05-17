using PointCloudConverter.Logger;
using PointCloudConverter.Plugins;
using PointCloudConverter.Readers;
using PointCloudConverter.Structs;
using PointCloudConverter.Writers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PointCloudConverter
{
    public static class ArgParser
    {
        const char argValueSeparator = '=';

        [DllImport("shell32.dll", SetLastError = true)]
        static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr hMem);

        public static string[] SplitArgs(string unsplitArgumentLine)
        {
            int numberOfArgs;
            IntPtr ptrToSplitArgs;
            string[] splitArgs;

            ptrToSplitArgs = CommandLineToArgvW(unsplitArgumentLine, out numberOfArgs);

            // CommandLineToArgvW returns NULL upon failure.
            if (ptrToSplitArgs == IntPtr.Zero)
                throw new ArgumentException("Unable to split argument.", new Win32Exception());

            // Make sure the memory ptrToSplitArgs to is freed, even upon failure.
            try
            {
                splitArgs = new string[numberOfArgs];

                // ptrToSplitArgs is an array of pointers to null terminated Unicode strings.
                // Copy each of these strings into our split argument array.
                for (int i = 0; i < numberOfArgs; i++)
                {
                    splitArgs[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptrToSplitArgs, i * IntPtr.Size));
                }

                return splitArgs;
            }
            finally
            {
                // Free memory obtained by CommandLineToArgW.
                LocalFree(ptrToSplitArgs);
            }
        }

        static string Reverse(string s)
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }

        static string GetEscapedCommandLine()
        {
            StringBuilder sb = new StringBuilder();
            bool gotQuote = false;
            foreach (var c in Environment.CommandLine.Reverse())
            {
                if (c == '"')
                    gotQuote = true;
                else if (gotQuote && c == '\\')
                {
                    // double it
                    sb.Append('\\');
                }
                else
                    gotQuote = false;

                sb.Append(c);
            }

            return Reverse(sb.ToString());
        }

        static ILogger Log;

        public static ImportSettings Parse(string[] args, string rootFolder, ILogger logger)
        {
            ImportSettings importSettings = new ImportSettings();
            Log = logger;

            // if there are any errors, they are added to this list, then importing is aborted after parsing arguments
            //List<string> errors = new List<string>();

            // handle commandline args (null is default args, not used)
            if (args == null)
            {
                args = SplitArgs(GetEscapedCommandLine()).Skip(1).ToArray();

                // if only single arg, -config=filename.txt, then read file and split args
                if (args.Length == 1 && args[0].ToLower().Contains("-config="))
                {
                    var configFile = args[0].Split(argValueSeparator)[1];
                    if (File.Exists(configFile) == true)
                    {
                        args = SplitArgs(File.ReadAllText(configFile).Trim()).Skip(1).ToArray();
                    }
                    else
                    {
                        importSettings.errors.Add("Config file not found: " + configFile);
                    }
                }
            }

            // parse commandline arguments
            if (args != null && args.Length > 0)
            {
                // folder backslash quote fix https://stackoverflow.com/a/9288040/5452781
                var realArgs = args;

                for (int i = 0; i < realArgs.Length; i++)
                {
                    var cmds = realArgs[i].ToLower().Split(argValueSeparator);

                    // FIXME cannot use contains, it could be in folder or filename
                    if (cmds[0].Contains("?") || cmds[0].ToLower().Contains("help"))
                    {
                        Tools.PrintHelpAndExit(argValueSeparator);
                    }

                    if (cmds != null && cmds.Length > 1)
                    {
                        var cmd = cmds[0];
                        var param = cmds[1];

                        //Console.WriteLine("cmd= " + cmd);

                        // to handle cs0206
                        int tempInt = -1;
                        float tempFloat = -1;

                        // check params
                        switch (cmd)
                        {
                            case "-importformat":
                                Log.Write("importformat = " + param);

                                string importFormatParsed = param.ToUpper();

                                if (string.IsNullOrEmpty(importFormatParsed) ||
                                    (importFormatParsed != "LAS" && importFormatParsed != "LAZ" && importFormatParsed != "PLY") && importFormatParsed != "E57")
                                {
                                    importSettings.errors.Add("Unsupported import format: " + param);
                                    importSettings.importFormat = ImportFormat.Unknown;
                                }
                                else
                                {
                                    switch (importFormatParsed)
                                    {
                                        case "LAS":
                                        case "LAZ":
                                            importSettings.importFormat = ImportFormat.LAS;
                                            importSettings.reader = new LAZ(null);
                                            break;
                                        case "PLY":
                                            importSettings.importFormat = ImportFormat.PLY;
                                            importSettings.reader = new PLY();
                                            break;
                                        case "E57":
                                            importSettings.importFormat = ImportFormat.E57;
                                            importSettings.reader = new E57();
                                            break;
                                    }
                                }
                                break;

                            case "-exportformat":
                                Log.Write("exportformat = " + param);

                                string exportFormatParsed = param.ToUpper();

                                // TODO check what writer interfaces are available
                                if (string.IsNullOrEmpty(exportFormatParsed) == true)
                                {
                                    importSettings.errors.Add("Unsupported export format: " + param);
                                    importSettings.exportFormat = ExportFormat.Unknown;
                                }
                                else // have some value
                                {
                                    // check built-in formats first
                                    switch (exportFormatParsed)
                                    {
                                        // TODO check enum names or interfaces
                                        case "PCROOT":
                                            importSettings.writer = new PCROOT(null);
                                            importSettings.exportFormat = ExportFormat.PCROOT;
                                            //importSettings.randomize = true; // required for V3, but if user wants to use it, they can disable it..
                                            break;
                                        case "UCPC":
                                            importSettings.writer = new UCPC();
                                            importSettings.exportFormat = ExportFormat.UCPC;
                                            break;
                                        default:
                                            //importSettings.errors.Add("Unknown export format: " + param);

                                            // TODO do we need to load it, or just check if dll exists?
                                            // check external plugin formats
                                            var writer = PluginLoader.LoadWriter(exportFormatParsed);
                                            if (writer != null)
                                            {
                                                importSettings.writer = writer;
                                                importSettings.exportFormat = ExportFormat.External; // For now, since its enum..
                                            }
                                            else
                                            {
                                                // Format is unknown, add to errors
                                                importSettings.errors.Add("Unknown export format: " + param);
                                                importSettings.exportFormat = ExportFormat.Unknown;
                                            }

                                            break;
                                    }
                                }
                                break;

                            case "-input":
                                Log.Write("input = " + param);

                                // remove quotes (needed for paths with spaces)
                                param = param.Trim('"');

                                // if relative folder, FIXME this fails on -input="C:\asdf\etryj\folder\" -importformat=las because backslash in \", apparently this https://stackoverflow.com/a/9288040/5452781
                                if (Path.IsPathRooted(param) == false)
                                {
                                    param = Path.Combine(rootFolder, param);
                                }

                                // check if its folder or file
                                if (Directory.Exists(param) == true)
                                {
                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Log.Write("Batch mode enabled (import whole folder)");
                                    Console.ForegroundColor = ConsoleColor.White;

                                    // TODO get file extension from commandline param? but then need to set -format before input.. for now only LAS/LAZ
                                    // TODO parse/sort args in required order, not in given order

                                    if (importSettings.importFormat == ImportFormat.Unknown)
                                    {
                                        importSettings.errors.Add("Import format not defined before -input folder for batch (use -importformat" + argValueSeparator + "LAS or PLY)");
                                    }
                                    else
                                    {
                                        string importExtensions = "";
                                        if (importSettings.importFormat == ImportFormat.LAS) importExtensions = "las|laz";
                                        if (importSettings.importFormat == ImportFormat.PLY) importExtensions = "ply";
                                        var filePaths = Directory.GetFiles(param).Where(file => Regex.IsMatch(file, @"^.+\.(" + importExtensions + ")$", RegexOptions.IgnoreCase)).ToArray();

                                        for (int j = 0; j < filePaths.Length; j++)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Gray;
                                            Log.Write("Found file: " + filePaths[j]);
                                            Console.ForegroundColor = ConsoleColor.White;
                                            importSettings.inputFiles.Add(filePaths[j]);
                                        }

                                    }
                                    importSettings.batch = true;

                                }
                                else // single file
                                {
                                    if (File.Exists(param) == false)
                                    {
                                        importSettings.errors.Add("(A) Input file not found: " + param);
                                    }
                                    else
                                    {
                                        // TODO check if compatible format
                                        //var ext = Path.GetExtension(param).ToLower();

                                        // TODO find better way to check all readers
                                        //if (ext == "las" ||ext == "laz")
                                        Log.Write("added " + param);
                                        importSettings.inputFiles.Add(param);
                                    }
                                }
                                break;

                            case "-output":
                                Log.Write("output = " + param);

                                // check if relative or not
                                if (Path.IsPathRooted(param) == false)
                                {
                                    param = Path.Combine(rootFolder, param);
                                }

                                // check if target is folder, but missing last slash, fix c:\data\here into c:\data\here\
                                if (Directory.Exists(param) && param.LastIndexOf(Path.DirectorySeparatorChar) != param.Length - 1)
                                {
                                    param += Path.DirectorySeparatorChar;
                                }

                                // no filename with extension, just output to folder with same name and new extension
                                if (string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(param)) == true)
                                {
                                    string inputFileName = null;

                                    // batch, so we dont have filename, Should use original base filenames then
                                    if (importSettings.batch == true)
                                    {
                                        // give timestamp name for now, if no name given
                                        //inputFileName = "batch_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                                    }
                                    else // single file
                                    {
                                        if (importSettings.inputFiles.Count > 0)
                                        {
                                            inputFileName = Path.GetFileNameWithoutExtension(importSettings.inputFiles[0]);
                                        }
                                    }

                                    // do we already set inputfile
                                    if (string.IsNullOrEmpty(inputFileName) == true)
                                    {
                                        // leavy empty for batch
                                        //errors.Add("-input not defined before -output or Input file doesnt exist, failed to create target filename");
                                    }
                                    else // have filename, create output filename from it by adding extension
                                    {
                                        // TODO use extension from selected export format, but we dont know it here yet?
                                        param = Path.Combine(param, inputFileName);// + ".ucpc");
                                    }
                                }
                                else // have output filename
                                {
                                    // check if target filename uses correct extension
                                    var extension = Path.GetExtension(param).ToLower();

                                    // FIXME cannot check version here.. otherwise args needs to be in correct order
                                    if (importSettings.exportFormat == ExportFormat.UCPC)
                                    {
                                        if (extension != ".ucpc")
                                        {
                                            // try to fix extension
                                            var ext = Path.GetFileNameWithoutExtension(param);
                                            if (string.IsNullOrEmpty(ext) == false)
                                            {
                                                param = param + ".ucpc";
                                            }
                                            else
                                            {
                                                importSettings.errors.Add("Invalid output file extension (must use .ucpc): " + extension);
                                            }
                                        }
                                    }
                                }

                                // check if target folder exists
                                var outputFolder = Path.GetDirectoryName(param);
                                if (Directory.Exists(outputFolder) == false)
                                {
                                    importSettings.errors.Add("Output directory not found: " + outputFolder);
                                }
                                else // we have output folder
                                {
                                    importSettings.outputFile = param;
                                }
                                break;

                            case "-scale":
                                Log.Write("scale = " + param);
                                param = param.Replace(",", ".");
                                bool parsedScale = float.TryParse(param, NumberStyles.Float, CultureInfo.InvariantCulture, out tempFloat);
                                if (parsedScale == false)
                                {
                                    importSettings.errors.Add("Invalid scale parameter: " + param);
                                }
                                else // got value
                                {
                                    if (importSettings.scale <= 0)
                                    {
                                        importSettings.errors.Add("Scale must be bigger than 0 : " + param);
                                    }
                                    else
                                    {
                                        importSettings.useScale = true;
                                        importSettings.scale = tempFloat;
                                    }
                                }
                                break;

                            case "-swap":
                                Log.Write("swap = " + param);

                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid swap parameter: " + param);
                                }
                                else
                                {
                                    importSettings.swapYZ = param == "true";
                                }
                                break;

                            case "-customintensityrange":
                                Log.Write("customintensityrange = " + param);

                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid useCustomIntensityRange parameter: " + param);
                                }
                                else
                                {
                                    importSettings.useCustomIntensityRange = param == "true";
                                }
                                break;

                            case "-invertx":
                                Log.Write("invertx = " + param);

                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid invertx parameter: " + param);
                                }
                                else
                                {
                                    importSettings.invertX = param == "true";
                                }
                                break;

                            case "-invertz":
                                Log.Write("invertz = " + param);

                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid invertz parameter: " + param);
                                }
                                else
                                {
                                    importSettings.invertZ = param == "true";
                                }
                                break;

                            case "-pack":
                                Log.Write("pack = " + param);

                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid pack parameter: " + param);
                                }
                                else
                                {
                                    importSettings.packColors = (param == "true");
                                }
                                break;

                            case "-packmagic":
                                Log.Write("packmagic = " + param);
                                bool packMagicParsed = int.TryParse(param, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempInt);
                                if (packMagicParsed == false || tempInt < 1)
                                {
                                    importSettings.errors.Add("Invalid packmagic parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.packMagicValue = tempInt;
                                    // ok
                                }
                                break;

                            case "-skip":
                                Log.Write("skip = " + param);
                                bool skipParsed = int.TryParse(param, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempInt);
                                if (skipParsed == false || tempInt < 2)
                                {
                                    importSettings.errors.Add("Invalid skip parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.skipEveryN = tempInt;
                                    importSettings.skipPoints = true;
                                }
                                break;

                            case "-keep":
                                Log.Write("keep = " + param);
                                bool keepParsed = int.TryParse(param, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempInt);
                                if (keepParsed == false || tempInt < 2)
                                {
                                    importSettings.errors.Add("Invalid keep parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.keepPoints = true;
                                    importSettings.keepEveryN = tempInt;
                                }
                                break;

                            case "-maxfiles":
                                Log.Write("maxfiles = " + param);
                                bool maxFilesParsed = int.TryParse(param, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempInt);
                                if (maxFilesParsed == false)
                                {
                                    importSettings.errors.Add("Invalid maxfiles parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.maxFiles = tempInt;
                                }
                                break;

                            case "-maxthreads":
                                Log.Write("maxthreads = " + param);
                                string cleanParam = param.Trim().TrimEnd('%');
                                bool maxThreadsParsed = int.TryParse(cleanParam, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempInt);
                                if (maxThreadsParsed == false)
                                {
                                    importSettings.errors.Add("Invalid maxthreads parameter: " + param);
                                }
                                else // got value (integer or int with percentage)
                                {
                                    if (param.IndexOf("%") > -1)
                                    {
                                        importSettings.maxThreads = (int)Math.Ceiling(Environment.ProcessorCount * (tempInt / 100f));
                                    }
                                    else
                                    {
                                        importSettings.maxThreads = tempInt;
                                    }

                                    if (importSettings.maxThreads < 1)
                                    {
                                        importSettings.errors.Add("Invalid maxthreads parameter, must be greater than 0: " + param);
                                    }

                                    if (importSettings.maxThreads > Environment.ProcessorCount)
                                    {
                                        importSettings.errors.Add("Maxthreads cannot be more than available processors: " + param);
                                    }
                                }
                                break;

                            case "-metadata":
                                Log.Write("metadata = " + param);
                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid metadata parameter: " + param);
                                }
                                else
                                {
                                    importSettings.importMetadata = param == "true";
                                }
                                break;

                            case "-metadataonly":
                                Log.Write("metadataonly = " + param);
                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid metadataonly parameter: " + param);
                                }
                                else
                                {
                                    importSettings.importMetadataOnly = param == "true";
                                }
                                break;

                            case "-averagetimestamp":
                                Log.Write("averagetimestamp = " + param);
                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid averagetimestamp parameter: " + param);
                                }
                                else
                                {
                                    importSettings.averageTimestamp = param == "true";
                                }
                                break;

                            case "-checkoverlap":
                                Log.Write("checkoverlap = " + param);
                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid checkoverlap parameter: " + param);
                                }
                                else
                                {
                                    importSettings.checkoverlap = param == "true";
                                }
                                break;

                            case "-json":
                                Log.Write("json = " + param);

                                if (param != "true" && param != "false")
                                {
                                    importSettings.errors.Add("Invalid json parameter: " + param);
                                }
                                else
                                {
                                    importSettings.useJSONLog = param == "true";
                                }
                                break;

                            case "-seed":
                                Log.Write("seed = " + param);
                                bool seedParsed = int.TryParse(param, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempInt);
                                if (seedParsed == false)
                                {
                                    importSettings.errors.Add("Invalid seed parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.seed = tempInt;
                                }
                                break;

                            case "-offset":
                                Log.Write("offset = " + param);

                                // check if its true or false (for automatic offset)
                                if (param != "false" && param != "true")
                                {
                                    // check if have x,y,z values, NOTE should be in this format: -offset=10.5,-123,0
                                    if (param.IndexOf(',') > -1)
                                    {
                                        var temp = param.Split(',');
                                        if (temp.Length == 3)
                                        {
                                            float xOff, yOff, zOff;
                                            if (float.TryParse(temp[0].Trim(), out xOff) && float.TryParse(temp[1].Trim(), out yOff) && float.TryParse(temp[2].Trim(), out zOff))
                                            {
                                                importSettings.manualOffsetX = -xOff;
                                                importSettings.manualOffsetY = -yOff;
                                                importSettings.manualOffsetZ = -zOff;
                                                importSettings.useManualOffset = true;
                                                importSettings.useAutoOffset = false;
                                            }
                                            else
                                            {
                                                importSettings.errors.Add("Invalid manual offset parameters for x,y,z: " + param);
                                            }
                                        }
                                        else
                                        {
                                            importSettings.errors.Add("Wrong amount of manual offset parameters for x,y,z: " + param);
                                        }
                                    }
                                    else
                                    {
                                        importSettings.errors.Add("Invalid offset parameter: " + param);
                                    }
                                }
                                else // autooffset
                                {
                                    importSettings.useAutoOffset = (param == "true");
                                    importSettings.useManualOffset = false;
                                }
                                break;

                            case "-limit":
                                Log.Write("limit = " + param);
                                // TODO add option to use percentage
                                bool limitParsed = int.TryParse(param, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempInt);
                                if (limitParsed == false || tempInt <= 0)
                                {
                                    importSettings.errors.Add("Invalid limit parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.useLimit = true;
                                    importSettings.limit = tempInt;
                                }
                                break;

                            case "-gridsize":
                                Log.Write("gridsize = " + param);
                                bool gridSizeParsed = float.TryParse(param, out tempFloat);
                                if (gridSizeParsed == false || tempFloat < 0.01f)
                                {
                                    importSettings.errors.Add("Invalid gridsize parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.gridSize = tempFloat;
                                }
                                break;

                            case "-minpoints":
                                Log.Write("minPoints = " + param);
                                bool minpointsParsed = int.TryParse(param, NumberStyles.Integer, CultureInfo.InvariantCulture, out tempInt);
                                if (minpointsParsed == false || tempInt < 1)
                                {
                                    importSettings.errors.Add("Invalid minpoints parameter: " + param + " (should be >0)");
                                }
                                else // got value
                                {
                                    importSettings.minimumPointCount = tempInt;
                                }
                                break;

                            case "-randomize":
                                Log.Write("randomize = " + param);

                                if (param != "false" && param != "true")
                                {
                                    importSettings.errors.Add("Invalid randomize parameter: " + param);
                                }
                                else
                                {
                                    importSettings.randomize = (param == "true");
                                }
                                break;

                            case "-usegrid":
                                Log.Write("usegrid = " + param);

                                if (param != "false" && param != "true")
                                {
                                    importSettings.errors.Add("Invalid usegrid parameter: " + param);
                                }
                                else
                                {
                                    importSettings.useGrid = (param == "true");
                                }
                                break;

                            case "-rgb":
                                Log.Write("rgb = " + param);

                                if (param != "false" && param != "true")
                                {
                                    importSettings.errors.Add("Invalid rgb parameter: " + param);
                                }
                                else
                                {
                                    importSettings.importRGB = (param == "true");
                                }
                                break;

                            case "-intensity":
                                Log.Write("intensity = " + param);

                                if (param != "false" && param != "true")
                                {
                                    importSettings.errors.Add("Invalid intensity parameter: " + param);
                                }
                                else
                                {
                                    importSettings.importIntensity = (param == "true");
                                }
                                break;

                            case "-classification":
                                Log.Write("classification = " + param);

                                if (param != "false" && param != "true")
                                {
                                    importSettings.errors.Add("Invalid classification parameter: " + param);
                                }
                                else
                                {
                                    importSettings.importClassification = (param == "true");
                                }
                                break;

                            case "-offsetmode":
                                Log.Write("offsetmode = " + param);

                                if (param != "legacy" && param != "min")
                                {
                                    importSettings.errors.Add("Invalid offsetmode parameter: " + param);
                                }
                                else
                                {
                                    importSettings.offsetMode = param;
                                }
                                break;

                            case "-filter":
                                Log.Write("filter = " + param);

                                bool filterDistValue = float.TryParse(param, out tempFloat);
                                if (filterDistValue == false || tempFloat <= 0f)

                                {
                                    importSettings.errors.Add("Invalid filter value (must be greater than 0) : " + param);
                                }
                                else
                                {
                                    importSettings.useFilter = true;
                                    importSettings.filterDistance = tempFloat;
                                }
                                break;

                            // TODO load whole commandline args list from text file
                            case "-config":
                                Log.Write("config = " + param);
                                // we dont do anything, config is checked at start of Parse()
                                //if (File.Exists(param) == false)
                                //{
                                //    importSettings.errors.Add("Config file not found: " + param);
                                //}
                                //else // got value, 
                                //{
                                //    //importSettings.config = param;
                                //}
                                break;

                            case "?":
                            case "/?":
                            case "help":
                            case "/help":
                            case "-help":
                            case "-?":
                                Tools.PrintHelpAndExit(argValueSeparator);
                                break;

                            default:
                                importSettings.errors.Add("Unrecognized argument: " + cmd + argValueSeparator + param);
                                break;
                        } // switch
                    }
                    else // bad arg (often due to missing "" around path with spaces)
                    {
                        importSettings.errors.Add("Unknown argument: " + cmds[0]);
                    }
                } // for args
            }
            else // if no commandline args
            {
                Tools.PrintHelpAndExit(argValueSeparator, waitEnter: true);
            }

            // check that we had input
            if (importSettings.inputFiles.Count == 0 || string.IsNullOrEmpty(importSettings.inputFiles[0]) == true)
            {
                importSettings.errors.Add("No input file(s) defined OR input folder is empty (use -input" + argValueSeparator + "yourfile.las or -input" + argValueSeparator + "yourfolder/)");
            }
            else // have input
            {
                if (importSettings.batch == true)
                {
                    Log.Write("Found " + importSettings.inputFiles.Count + " files..");

                    // if no output folder given at all
                    if (string.IsNullOrEmpty(importSettings.outputFile) == true)
                    {
                        if (importSettings.exportFormat == ExportFormat.UCPC)
                        {
                            // we'll use same folder as input then
                            if (importSettings.inputFiles != null && importSettings.inputFiles.Count > 1)
                            {
                                importSettings.outputFile = Path.GetDirectoryName(importSettings.inputFiles[0]) + Path.DirectorySeparatorChar;
                                Log.Write("importSettings.outputFile=" + importSettings.outputFile);
                            }
                            else
                            {
                                importSettings.errors.Add("(D) No input files found, so cannot determine output folder either..");
                            }
                        }
                        else if (importSettings.exportFormat == ExportFormat.PCROOT)
                        {
                            // we should ask for export folder, otherwise source folder is filled with files
                            importSettings.errors.Add("(C) -output file or folder not defined (its required for V3 PCROOT format)");
                        }
                    }
                    else // have something in output field
                    {
                        // check if output is folder
                        if (Directory.Exists(importSettings.outputFile) == true)
                        {
                            if (importSettings.exportFormat == ExportFormat.PCROOT)
                            {
                                importSettings.errors.Add("(E) PCROOT Requires some output filename (example: output.pcroot)");
                            }
                            if (importSettings.exportFormat == ExportFormat.External && importSettings.batch == false)
                            {
                                importSettings.errors.Add("(E2) External formats require some output filename for non-batch operations (example: basefilename)");
                            }
                        }
                    }

                }
                else // not in batch
                {
                    // check if first file exists
                    if (File.Exists(importSettings.inputFiles[0]) == false)
                    {
                        importSettings.errors.Add("(B) Input file not found: " + importSettings.inputFiles[0]);
                    }

                    // if no output folder/file defined, put in same folder as source
                    if (string.IsNullOrEmpty(importSettings.outputFile) == true)
                    {
                        // FIXME handles v2 output only now
                        var outputFolder = Path.GetDirectoryName(importSettings.inputFiles[0]);
                        var outputFilename = Path.GetFileNameWithoutExtension(importSettings.inputFiles[0]);
                        importSettings.outputFile = Path.Combine(outputFolder, outputFilename + ".ucpc");
                    }
                }
            } // have input

            // check required settings
            if (importSettings.exportFormat == ExportFormat.Unknown)
            {
                importSettings.errors.Add("No export format defined (Example: -exportformat" + argValueSeparator + "PCROOT)");
            }

            // cannot have both rgb & intensity
            //if (importSettings.importRGB == true && importSettings.importIntensity == true)
            //{
            //    importSettings.errors.Add("Cannot have both -rgb and -intensity enabled");
            //}            

            // must have at least one
            if (importSettings.importRGB == false && importSettings.importIntensity == false && importSettings.importClassification == false)
            {
                importSettings.errors.Add("Must have -rgb OR -intensity OR -classification enabled");
            }

            // but cannot have int and class only
            if (importSettings.importRGB == false && importSettings.importIntensity == true && importSettings.importClassification == true)
            {
                importSettings.errors.Add("Cannot have -intensity and -classification enabled without -rgb");
            }

            if (importSettings.exportFormat == ExportFormat.UCPC && importSettings.maxThreads > 1)
            {
                importSettings.errors.Add("UCPC format doesnt support multi-threading yet, use 1 thread only (or remove -maxthreads param)");
            }

            //// check mismatching settings for v2 vs v3
            //if (importSettings.exportFormat == ExportFormat.UCPC)
            //{
            //    //if (importSettings.gridSize)
            //}

            if (importSettings.batch == true && importSettings.exportFormat == ExportFormat.UCPC && Path.GetExtension(importSettings.outputFile).ToLower() == ".ucpc")
            {
                importSettings.errors.Add("With batch processing whole input folder, do not set output filename - Set output folder (each .UCPP file will be saved separately)");
            }

            if (importSettings.batch == true && importSettings.exportFormat == ExportFormat.External && Path.GetExtension(importSettings.outputFile).ToLower() == ".glb")
            {
                importSettings.errors.Add("With batch processing whole input folder, do not set output filename - Set output folder (each .GLB file will be saved separately)");
            }

            if (importSettings.skipPoints == true && importSettings.keepPoints == true)
            {
                importSettings.errors.Add("Cannot have both -keep and -skip enabled");
            }

            if (importSettings.importFormat == ImportFormat.Unknown)
            {
                importSettings.importFormat = ImportFormat.LAS;
                importSettings.reader = new LAZ(null);
                Log.Write("No import format defined, using Default: " + importSettings.importFormat.ToString());
            }

            if (importSettings.importFormat == ImportFormat.PLY)
            {
                if (importSettings.importIntensity || importSettings.importClassification) Log.Write("PLY doesnt support intensity or classification importing.");
                if (importSettings.packColors) Log.Write("PLY doesnt support color packing.");
            }

            if (importSettings.exportFormat == ExportFormat.PCROOT && importSettings.useGrid == false)
            {
                //importSettings.errors.Add("V3 pcroot export format requires -usegrid=true to use grid");
                Log.Write("V3 pcroot export format requires -usegrid=true to use grid, enabling it now.");
                importSettings.useGrid = true;
            }


            // disable this error, if user really wants to use it
            //if (importSettings.randomize == false && importSettings.exportFormat == ExportFormat.PCROOT)
            //{
            //    importSettings.errors.Add("V3 pcroot export format requires -randomize=true to randomize points");
            //}

            //if (decimatePoints == true && (skipPoints == true || keepPoints == true))
            //{
            //    importSettings.errors.Add("Cannot use -keep or -skip when using -decimate");
            //}

            if (importSettings.errors.Count > 0) importSettings.haveError = true;

            // if using jsonlog, print import settings
            if (importSettings.useJSONLog == true)
            {
                // TODO workaround to get logevent in this json data (not used later)
                //importSettings.version = Log.version;
                importSettings.@event = Logger.LogEvent.Settings;
                Log.Write(importSettings.ToJSON(), Logger.LogEvent.Settings);
            }

            // show errors
            if (importSettings.errors.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Log.Write("\nErrors found:");
                Console.ForegroundColor = ConsoleColor.Red;
                for (int i = 0; i < importSettings.errors.Count; i++)
                {
                    Log.Write(i + "> " + importSettings.errors[i]);
                }
                Console.ForegroundColor = ConsoleColor.White;

                //if (importSettings.useJSONLog == true)
                //{
                //    // convert errors list to json
                //    importSettings.logEvent = Logger.LogEvent.Error;
                //    var json = JsonSerializer.Serialize(importSettings.errors);
                //}
                Environment.ExitCode = (int)ExitCode.Error;
            }

            // return always, but note that we might have errors
            return importSettings;
        }
    }
}
