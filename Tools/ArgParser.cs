﻿using PointCloudConverter.Readers;
using PointCloudConverter.Structs;
using PointCloudConverter.Writers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PointCloudConverter
{
    public class ArgParser
    {
        const char argValueSeparator = '=';

        public static ImportSettings Parse(string[] args, string rootFolder)
        {

            ImportSettings importSettings = new ImportSettings();

            // if there are any errors, they are added to this list, then importing is aborted after parsing arguments
            List<string> errors = new List<string>();

            // parse commandline arguments
            if (args != null && args.Length > 0)
            {
                var realArgs = args;
                for (int i = 0; i < realArgs.Length; i++)
                {
                    // FIXME this fails on -input="C:\asdf\etryj\folder\" -importformat=las
                    // it becomes          -input=C:\asdf\etryj\folder" -importformat=las
                    // because backslash in \", apparently https://stackoverflow.com/a/9288040/5452781
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

                        // check params
                        switch (cmd)
                        {
                            case "-importformat":
                                Console.WriteLine("importformat = " + param);

                                string importFormatParsed = param.ToUpper();

                                // TODO check what reader interfaces are available
                                if (string.IsNullOrEmpty(importFormatParsed) == true || (importFormatParsed != "LAS" && importFormatParsed != "LAZ"))
                                {
                                    errors.Add("Unsupported import format: " + param);
                                    importSettings.importFormat = ImportFormat.Unknown;
                                }
                                else
                                {
                                    importSettings.importFormat = ImportFormat.LAS;
                                    importSettings.reader = new LAZ();
                                }
                                break;
                            case "-exportformat":
                                Console.WriteLine("exportformat = " + param);

                                string exportFormatParsed = param.ToUpper();

                                // TODO check what writer interfaces are available
                                if (string.IsNullOrEmpty(exportFormatParsed) == true || (exportFormatParsed != "UCPC" && exportFormatParsed != "PCROOT"))
                                {
                                    errors.Add("Unsupported export format: " + param);
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
                                            break;
                                        default:
                                            importSettings.writer = new UCPC();
                                            importSettings.exportFormat = ExportFormat.UCPC;
                                            break;
                                    }
                                }
                                break;

                            case "-input":
                                Console.WriteLine("input = " + param);

                                // if relative folder, FIXME this fails on -input="C:\asdf\etryj\folder\" -importformat=las because backslash in \", apparently this https://stackoverflow.com/a/9288040/5452781
                                if (Path.IsPathRooted(param) == false)
                                {
                                    param = Path.Combine(rootFolder, param);
                                }

                                // check if its folder or file
                                if (Directory.Exists(param) == true)
                                {
                                    Console.ForegroundColor = ConsoleColor.Gray;
                                    Console.WriteLine("Batch mode enabled (import whole folder)");
                                    Console.ForegroundColor = ConsoleColor.White;

                                    // TODO get file extension from commandline param? but then need to set -format before input.. for now only LAS/LAZ
                                    // TODO parse/sort args in required order, not in given order
                                    var filePaths = Directory.GetFiles(param).Where(file => Regex.IsMatch(file, @"^.+\.(las|laz)$", RegexOptions.IgnoreCase)).ToArray();

                                    for (int j = 0; j < filePaths.Length; j++)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Gray;
                                        Console.WriteLine("Added file: " + filePaths[j]);
                                        Console.ForegroundColor = ConsoleColor.White;
                                        importSettings.inputFiles.Add(filePaths[j]);
                                    }

                                    importSettings.batch = true;
                                }
                                else // single file
                                {
                                    if (File.Exists(param) == false)
                                    {
                                        errors.Add("(A) Input file not found: " + param);
                                    }
                                    else
                                    {
                                        importSettings.inputFiles.Add(param);
                                    }
                                }
                                break;

                            case "-output":
                                Console.WriteLine("output = " + param);

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

                                    // batch, so we dont have filename
                                    if (importSettings.batch == true)
                                    {
                                        // give timestamp name for now, if no name given
                                        inputFileName = "batch_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                                    }
                                    else // single file
                                    {
                                        inputFileName = Path.GetFileNameWithoutExtension(importSettings.inputFiles[0]);
                                    }

                                    // had we already set inputfile
                                    if (string.IsNullOrEmpty(inputFileName) == true)
                                    {
                                        errors.Add("-input not defined before -output, failed to create target filename");
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
                                                errors.Add("Invalid output file extension (must use .ucpc): " + extension);
                                            }
                                        }
                                    }
                                }

                                // check if target folder exists
                                var outputFolder = Path.GetDirectoryName(param);
                                if (Directory.Exists(outputFolder) == false)
                                {
                                    errors.Add("Output directory not found: " + outputFolder);
                                }
                                else // we have output folder
                                {
                                    importSettings.outputFile = param;
                                }
                                break;

                            case "-scale":
                                Console.WriteLine("scale = " + param);

                                bool parsedScale = float.TryParse(param, out importSettings.scale);
                                if (parsedScale == false)
                                {
                                    errors.Add("Invalid scale parameter: " + param);
                                }
                                else // got value
                                {
                                    if (importSettings.scale <= 0)
                                    {
                                        errors.Add("Scale must be bigger than 0 : " + param);
                                    }
                                    else
                                    {
                                        importSettings.useScale = true;
                                    }
                                }
                                break;

                            case "-swap":
                                Console.WriteLine("swap = " + param);

                                if (param != "true" && param != "false")
                                {
                                    errors.Add("Invalid swap parameter: " + param);
                                }
                                else
                                {
                                    importSettings.swapYZ = (param == "true");
                                }
                                break;

                            case "-pack":
                                Console.WriteLine("pack = " + param);

                                if (param != "true" && param != "false")
                                {
                                    errors.Add("Invalid pack parameter: " + param);
                                }
                                else
                                {
                                    importSettings.packColors = (param == "true");
                                }
                                break;

                            case "-packmagic":
                                Console.WriteLine("packmagic = " + param);
                                bool packMagicParsed = int.TryParse(param, out importSettings.packMagicValue);
                                if (packMagicParsed == false || importSettings.packMagicValue < 1)
                                {
                                    errors.Add("Invalid packmagic parameter: " + param);
                                }
                                else // got value
                                {
                                    // ok
                                }
                                break;

                            case "-skip":
                                Console.WriteLine("skip = " + param);
                                bool skipParsed = int.TryParse(param, out importSettings.skipEveryN);
                                if (skipParsed == false || importSettings.skipEveryN < 2)
                                {
                                    errors.Add("Invalid skip parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.skipPoints = true;
                                }
                                break;

                            case "-keep":
                                Console.WriteLine("keep = " + param);
                                bool keepParsed = int.TryParse(param, out importSettings.keepEveryN);
                                if (keepParsed == false || importSettings.keepEveryN < 2)
                                {
                                    errors.Add("Invalid keep parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.keepPoints = true;
                                }
                                break;

                            case "-maxfiles":
                                Console.WriteLine("maxfiles = " + param);
                                bool maxFilesParsed = int.TryParse(param, out importSettings.maxFiles);
                                if (maxFilesParsed == false)
                                {
                                    errors.Add("Invalid maxfiles parameter: " + param);
                                }
                                else // got value
                                {
                                    // ok
                                }
                                break;

                            case "-offset":
                                Console.WriteLine("offset = " + param);

                                if (param != "false" && param != "true")
                                {
                                    errors.Add("Invalid offset parameter: " + param);
                                }
                                else
                                {
                                    importSettings.useAutoOffset = (param == "true");
                                }
                                break;

                            case "-limit":
                                Console.WriteLine("limit = " + param);
                                // TODO add option to use percentage
                                bool limitParsed = int.TryParse(param, out importSettings.limit);
                                if (limitParsed == false || importSettings.limit <= 0)
                                {
                                    errors.Add("Invalid limit parameter: " + param);
                                }
                                else // got value
                                {
                                    importSettings.useLimit = true;
                                }
                                break;

                            case "-gridsize":
                                Console.WriteLine("gridsize = " + param);

                                bool gridSizeParsed = float.TryParse(param, out importSettings.gridSize);
                                if (gridSizeParsed == false || importSettings.gridSize < 0.01f)
                                {
                                    errors.Add("Invalid gridsize parameter: " + param);
                                }
                                else // got value
                                {
                                    // ok
                                }
                                break;

                            case "-minpoints":
                                Console.WriteLine("minPoints = " + param);
                                bool minpointsParsed = int.TryParse(param, out importSettings.minimumPointCount);
                                if (minpointsParsed == false || importSettings.minimumPointCount <= 1)
                                {
                                    errors.Add("Invalid minpoints parameter: " + param + " (should be >1)");
                                }
                                else // got value
                                {
                                    // ok
                                }
                                break;

                            case "-randomize":
                                Console.WriteLine("randomize = " + param);

                                if (param != "false" && param != "true")
                                {
                                    errors.Add("Invalid randomize parameter: " + param);
                                }
                                else
                                {
                                    importSettings.randomize = (param == "true");
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
                                errors.Add("Unrecognized option: " + cmd + ":" + param);
                                break;
                        }
                    }
                }
            }
            else // if no commandline args
            {
                Tools.PrintHelpAndExit(argValueSeparator, waitEnter: true);
            }

            // check that we had input
            if (importSettings.inputFiles.Count == 0 || string.IsNullOrEmpty(importSettings.inputFiles[0]) == true)
            {
                errors.Add("No input file(s) defined (use -input" + argValueSeparator + "yourfile.las)");
            }
            else // have input
            {
                if (importSettings.batch == true)
                {
                    Console.WriteLine("Found " + importSettings.inputFiles.Count + " files..");
                }
                else // not in batch
                {
                    if (File.Exists(importSettings.inputFiles[0]) == false)
                    {
                        errors.Add("(B) Input file not found: " + importSettings.inputFiles[0]);
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
                errors.Add("No export format defined (Example: -exportformat" + argValueSeparator + "UCPC)");
            }

            if (importSettings.batch == true && importSettings.exportFormat != ExportFormat.PCROOT)
            {
                errors.Add("Folder batch is only supported for PCROOT (v3) version: -exportformat=pcroot");
            }

            if (importSettings.skipPoints == true && importSettings.keepPoints == true)
            {
                errors.Add("Cannot have both -keep and -skip enabled");
            }

            if (importSettings.importFormat == ImportFormat.Unknown)
            {
                importSettings.importFormat = ImportFormat.LAS;
                importSettings.reader = new LAZ();
                Console.WriteLine("No import format defined, using Default: " + importSettings.importFormat.ToString());
            }

            //if (decimatePoints == true && (skipPoints == true || keepPoints == true))
            //{
            //    errors.Add("Cannot use -keep or -skip when using -decimate");
            //}

            // show errors
            if (errors.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nErrors found:");
                Console.ForegroundColor = ConsoleColor.Red;
                for (int i = 0; i < errors.Count; i++)
                {
                    Console.WriteLine(i + "> " + errors[i]);
                }
                Console.ForegroundColor = ConsoleColor.White;
                return null;
            }
            else
            {
                return importSettings;
            }
        }
    }
}
