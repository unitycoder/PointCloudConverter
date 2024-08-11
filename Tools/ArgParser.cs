using PointCloudConverter.Readers;
using PointCloudConverter.Structs;
using PointCloudConverter.Writers;
using System;
using System.ComponentModel;
using System.Diagnostics;
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

        static string[] SplitArgs(string unsplitArgumentLine)
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

        public static ImportSettings Parse(string[] args, string rootFolder)
        {
            ImportSettings importSettings = new ImportSettings();

            // if there are any errors, they are added to this list, then importing is aborted after parsing arguments
            //List<string> errors = new List<string>();

            // handle manual args (null is default args, not used)
            if (args == null) args = SplitArgs(GetEscapedCommandLine()).Skip(1).ToArray();

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
                                Log.WriteLine("importformat = " + param);

                                string importFormatParsed = param.ToUpper();

                                // TODO check what reader interfaces are available
                                if (string.IsNullOrEmpty(importFormatParsed) == true || (importFormatParsed != "LAS" && importFormatParsed != "LAZ"))
                                {
                                    importSettings.errors.Add("Unsupported import format: " + param);
                                    importSettings.importFormat = ImportFormat.Unknown;
                                }
                                else
                                {
                                    importSettings.importFormat = ImportFormat.LAS;
                                    importSettings.reader = new LAZ();
                                }
                                break;
                            case "-exportformat":
                                Log.WriteLine("exportformat = " + param);

                                string exportFormatParsed = param.ToUpper();

                                // TODO check what writer interfaces are available
                                if (string.IsNullOrEmpty(exportFormatParsed) == true || (exportFormatParsed != "UCPC" && exportFormatParsed != "PCROOT"))
                                {
                                    importSettings.errors.Add("Unsupported export format: " + param);
                                    importSettings.exportFormat = ExportFormat.Unknown;
                                }
                                else
                                {
                                    // TODO later needs more formats..
                                    switch (exportFormatParsed)
                                    {
                                        // TODO check enum names or interfaces
                                        case "PCROOT":
                                            importSettings.writer = new PCROOT();
                                            importSettings.exportFormat = ExportFormat.PCROOT;
                                            importSettings.randomize = true; // required for V3
                                            break;
                                        default:
                                            importSettings.writer = new UCPC();
                                            importSettings.exportFormat = ExportFormat.UCPC;
                                            break;
                                    }
                                }
                                break;

                            case "-input":
                                Log.WriteLine("input = " + param);

                                // if relative folder, FIXME this fails on -input="C:\asdf\etryj\folder\" -importformat=las because backslash in \", apparently this https://stackoverflow.com/a/9288040/5452781
                                if (Path.IsPathRooted(param) == false)
                                {
                                    param = Path.Combine(rootFolder, param);
                                }

                                // check if its folder or file
                                if (Directory.Exists(param) == true)
                                {
                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Log.WriteLine("Batch mode enabled (import whole folder)");
                                    Console.ForegroundColor = ConsoleColor.White;

                                    // TODO get file extension from commandline param? but then need to set -format before input.. for now only LAS/LAZ
                                    // TODO parse/sort args in required order, not in given order
                                    var filePaths = Directory.GetFiles(param).Where(file => Regex.IsMatch(file, @"^.+\.(las|laz)$", RegexOptions.IgnoreCase)).ToArray();


                                    for (int j = 0; j < filePaths.Length; j++)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Gray;
                                        Log.WriteLine("Found file: " + filePaths[j]);
                                        Console.ForegroundColor = ConsoleColor.White;
                                        importSettings.inputFiles.Add(filePaths[j]);
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
                                        Log.WriteLine("added " + param);
                                        importSettings.inputFiles.Add(param);
                                    }
                                }
                                break;

                            case "-output":
                                Log.WriteLine("output = " + param);

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

                                // no filename, just output to folder with same name and new extension
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
                                    else // have filename, create output filename from it
                                    {
                                        param = Path.Combine(param, inputFileName + ".ucpc");

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
                                Log.WriteLine("scale = " + param);

                                bool parsedScale = float.TryParse(param, out tempFloat);
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
                                Log.WriteLine("swap = " + param);

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
                                Log.WriteLine("customintensityrange = " + param);

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
                                Log.WriteLine("invertx = " + param);

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
                                Log.WriteLine("invertz = " + param);

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
                                Log.WriteLine("pack = " + param);

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
                                Log.WriteLine("packmagic = " + param);
                                bool packMagicParsed = int.TryParse(param, out tempInt);
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
                                Log.WriteLine("skip = " + param);
                                bool skipParsed = int.TryParse(param, out tempInt);
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
                                Log.WriteLine("keep = " + param);
                                bool keepParsed = int.TryParse(param, out tempInt);
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
                                Log.WriteLine("maxfiles = " + param);
                                bool maxFilesParsed = int.TryParse(param, out tempInt);
                                if (maxFilesParsed == false)
                                {
                                    importSettings.errors.Add("Invalid maxfiles parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.maxFiles = tempInt;
                                }
                                break;

                            case "-metadata":
                                Log.WriteLine("metadata = " + param);
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
                                Log.WriteLine("metadataonly = " + param);
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
                                Log.WriteLine("averagetimestamp = " + param);
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
                                Log.WriteLine("checkoverlap = " + param);
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
                                Log.WriteLine("json = " + param);

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
                                Log.WriteLine("seed = " + param);
                                bool seedParsed = int.TryParse(param, out tempInt);
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
                                Log.WriteLine("offset = " + param);

                                // check if its true or false
                                if (param != "false" && param != "true")
                                {
                                    // check if its valid integer x,z
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
                                Log.WriteLine("limit = " + param);
                                // TODO add option to use percentage
                                bool limitParsed = int.TryParse(param, out tempInt);
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
                                Log.WriteLine("gridsize = " + param);
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
                                Log.WriteLine("minPoints = " + param);
                                bool minpointsParsed = int.TryParse(param, out tempInt);
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
                                Log.WriteLine("randomize = " + param);

                                if (param != "false" && param != "true")
                                {
                                    importSettings.errors.Add("Invalid randomize parameter: " + param);
                                }
                                else
                                {
                                    importSettings.randomize = (param == "true");
                                }
                                break;

                            case "-rgb":
                                Log.WriteLine("rgb = " + param);

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
                                Log.WriteLine("intensity = " + param);

                                if (param != "false" && param != "true")
                                {
                                    importSettings.errors.Add("Invalid intensity parameter: " + param);
                                }
                                else
                                {
                                    importSettings.importIntensity = (param == "true");
                                }
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
                importSettings.errors.Add("No input file(s) defined (use -input" + argValueSeparator + "yourfile.las)");
            }
            else // have input
            {
                if (importSettings.batch == true)
                {
                    Log.WriteLine("Found " + importSettings.inputFiles.Count + " files..");

                    // if no output folder given
                    if (string.IsNullOrEmpty(importSettings.outputFile) == true)
                    {
                        if (importSettings.exportFormat == ExportFormat.UCPC)
                        {
                            // we'll use same folder as input then
                            if (importSettings.inputFiles != null && importSettings.inputFiles.Count > 1)
                            {
                                importSettings.outputFile = Path.GetDirectoryName(importSettings.inputFiles[0]) + Path.DirectorySeparatorChar;
                                Log.WriteLine("importSettings.outputFile=" + importSettings.outputFile);
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

                }
                else // not in batch
                {
                    if (File.Exists(importSettings.inputFiles[0]) == false)
                    {
                        importSettings.errors.Add("(B) Input file not found: " + importSettings.inputFiles[0]);
                    }

                    // if no output file defined, put in same folder as source
                    if (string.IsNullOrEmpty(importSettings.outputFile) == true)
                    {
                        // v2 output
                        var outputFolder = Path.GetDirectoryName(importSettings.inputFiles[0]);
                        var outputFilename = Path.GetFileNameWithoutExtension(importSettings.inputFiles[0]);
                        importSettings.outputFile = Path.Combine(outputFolder, outputFilename + ".ucpc");

                    }
                }
            }

            // check required settings
            if (importSettings.exportFormat == ExportFormat.Unknown)
            {
                importSettings.errors.Add("No export format defined (Example: -exportformat" + argValueSeparator + "UCPC)");
            }

            // cannot have both rgb & intensity
            //if (importSettings.importRGB == true && importSettings.importIntensity == true)
            //{
            //    importSettings.errors.Add("Cannot have both -rgb and -intensity enabled");
            //}            

            // must have at least one
            if (importSettings.importRGB == false && importSettings.importIntensity == false)
            {
                importSettings.errors.Add("Must have -rgb OR -intensity enabled");
            }

            //// check mismatching settings for v2 vs v3
            //if (importSettings.exportFormat == ExportFormat.UCPC)
            //{
            //    //if (importSettings.gridSize)
            //}

            //if (importSettings.batch == true && importSettings.exportFormat != ExportFormat.PCROOT)
            //{
            //    importSettings.errors.Add("Folder batch is only supported for PCROOT (v3) version: -exportformat=pcroot");
            //}

            if (importSettings.skipPoints == true && importSettings.keepPoints == true)
            {
                importSettings.errors.Add("Cannot have both -keep and -skip enabled");
            }

            if (importSettings.importFormat == ImportFormat.Unknown)
            {
                importSettings.importFormat = ImportFormat.LAS;
                importSettings.reader = new LAZ();
                Log.WriteLine("No import format defined, using Default: " + importSettings.importFormat.ToString());
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
                importSettings.version = Log.version;
                importSettings.@event = Logger.LogEvent.Settings;
                Log.WriteLine(importSettings.ToJSON(), Logger.LogEvent.Settings);
            }

            // show errors
            if (importSettings.errors.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Log.WriteLine("\nErrors found:");
                Console.ForegroundColor = ConsoleColor.Red;
                for (int i = 0; i < importSettings.errors.Count; i++)
                {
                    Log.WriteLine(i + "> " + importSettings.errors[i]);
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
