// UCPC (v2) Exporter https://github.com/unitycoder/UnityPointCloudViewer/wiki/Binary-File-Format-Structure#custom-v2-ucpc-binary-format

using PointCloudConverter.Logger;
using PointCloudConverter.Structs;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

namespace PointCloudConverter.Writers
{
    public class UCPC : IWriter
    {
        ImportSettings importSettings;
        int pointCount;

        BufferedStream bsPoints = null;
        BinaryWriter writerPoints = null;
        BufferedStream bsColorsV2 = null;
        BinaryWriter writerColorsV2 = null;
        BufferedStream bsHeaderV2 = null;
        BinaryWriter writerHeaderV2 = null;

        string pointsTempFile;
        string colorsTempFile;
        string headerTempFile;

        static float cloudMinX = float.PositiveInfinity;
        static float cloudMinY = float.PositiveInfinity;
        static float cloudMinZ = float.PositiveInfinity;
        static float cloudMaxX = float.NegativeInfinity;
        static float cloudMaxY = float.NegativeInfinity;
        static float cloudMaxZ = float.NegativeInfinity;

        bool IWriter.InitWriter(ImportSettings _importSettings, int _pointCount)
        {
            importSettings = _importSettings;
            pointCount = _pointCount;

            pointsTempFile = importSettings.outputFile + "_PointsTemp";
            colorsTempFile = importSettings.outputFile + "_ColorsTemp";
            headerTempFile = importSettings.outputFile + "_Header";

            if (importSettings.seed != -1) Tools.SetRandomSeed(importSettings.seed);

            cloudMinX = float.PositiveInfinity;
            cloudMinY = float.PositiveInfinity;
            cloudMinZ = float.PositiveInfinity;
            cloudMaxX = float.NegativeInfinity;
            cloudMaxY = float.NegativeInfinity;
            cloudMaxZ = float.NegativeInfinity;

            try
            {
                bsPoints = new BufferedStream(new FileStream(pointsTempFile, FileMode.Create, FileAccess.Write, FileShare.None));
                writerPoints = new BinaryWriter(bsPoints);

                bsColorsV2 = new BufferedStream(new FileStream(colorsTempFile, FileMode.Create, FileAccess.Write, FileShare.None));
                writerColorsV2 = new BinaryWriter(bsColorsV2);

                bsHeaderV2 = new BufferedStream(new FileStream(headerTempFile, FileMode.Create, FileAccess.Write, FileShare.None));
                writerHeaderV2 = new BinaryWriter(bsHeaderV2);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }

            return true;
        }

        void IWriter.CreateHeader(int pointCount)
        {
            // create header data file 
            // write header v2 : 34 bytes
            byte[] magic = new byte[] { 0x75, 0x63, 0x70, 0x63 }; // ucpc
            writerHeaderV2.Write(magic); // 4b magic
            if (importSettings.packColors == true)
            {
                writerHeaderV2.Write((byte)3); // 1b version
                writerHeaderV2.Write(false); // 1b contains RGB
            }
            else
            {
                writerHeaderV2.Write((byte)2); // 1b version
                writerHeaderV2.Write(true); // 1b contains RGB
            }

            if (importSettings.skipPoints == true)
            {
                pointCount = (int)Math.Floor(pointCount - (pointCount / (float)importSettings.skipEveryN));
                Console.WriteLine("skipPoints, total = " + (pointCount - (pointCount / (float)importSettings.skipEveryN)));
            }

            if (importSettings.keepPoints == true) pointCount = pointCount / importSettings.keepEveryN;

            writerHeaderV2.Write(pointCount); // 4b
            // output bounds 4+4+4+4+4+4
            writerHeaderV2.Write(cloudMinX);
            writerHeaderV2.Write(cloudMinY);
            writerHeaderV2.Write(cloudMinZ);
            writerHeaderV2.Write(cloudMaxX);
            writerHeaderV2.Write(cloudMaxY);
            writerHeaderV2.Write(cloudMaxZ);

            // close header
            writerHeaderV2.Close();
            bsHeaderV2.Dispose();
        }

        float prev_x, prev_y, prev_z;
        void IWriter.WriteXYZ(float x, float y, float z)
        {
            if (importSettings.packColors == true)
            {
                prev_x = x;
                prev_y = y;
                prev_z = z;
            }
            else
            {
                writerPoints.Write(x);
                writerPoints.Write(y);
                writerPoints.Write(z);
            }

        }

        void IWriter.WriteRGB(float r, float g, float b)
        {
            if (importSettings.packColors == true)
            {
                // pack red and x
                r = Tools.SuperPacker(r * 0.98f, prev_x, 1024); // NOTE fixed for now, until update shaders and header to contain packmagic value
                // pack green and y
                g = Tools.SuperPacker(g * 0.98f, prev_y, 1024);
                // pack blue and z
                b = Tools.SuperPacker(b * 0.98f, prev_z, 1024);
                writerPoints.Write(r);
                writerPoints.Write(g);
                writerPoints.Write(b);
            }
            else
            {
                writerColorsV2.Write(r);
                writerColorsV2.Write(g);
                writerColorsV2.Write(b);
            }
        }

        void IWriter.Randomize()
        {
            writerPoints.Flush();
            bsPoints.Flush();
            writerPoints.Close();
            bsPoints.Dispose();

            Log.WriteLine("Randomizing " + pointCount + " points...");
            // randomize points and colors
            byte[] tempBytes = null;
            using (FileStream fs = File.Open(pointsTempFile, FileMode.Open, FileAccess.Read, FileShare.None))
            using (BufferedStream bs = new BufferedStream(fs))
            using (BinaryReader binaryReader = new BinaryReader(bs))
            {
                tempBytes = binaryReader.ReadBytes(pointCount * 4 * 3);
            }
            float[] tempFloats = new float[pointCount * 3];

            // convert to float array
            GCHandle vectorPointer = GCHandle.Alloc(tempFloats, GCHandleType.Pinned);
            IntPtr pV = vectorPointer.AddrOfPinnedObject();
            Marshal.Copy(tempBytes, 0, pV, pointCount * 4 * 3);
            vectorPointer.Free();

            Tools.ResetRandom();
            Tools.ShuffleXYZ(ref tempFloats);
            // need to reset random to use same seed
            Tools.ResetRandom();

            // create new file on top, NOTE seek didnt work?
            bsPoints = new BufferedStream(new FileStream(pointsTempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.Read));
            writerPoints = new BinaryWriter(bsPoints);

            // TODO why not use writeallbytes?
            for (int i = 0; i < pointCount * 3; i++)
            {
                writerPoints.Write(tempFloats[i]);
            }

            if (importSettings.packColors == true)
            {

            }
            else
            {
                // new files for colors
                writerColorsV2.Flush();
                bsColorsV2.Flush();
                writerColorsV2.Close();
                bsColorsV2.Dispose();

                Log.WriteLine("Randomizing " + pointCount + " colors...");

                tempBytes = null;
                using (FileStream fs = File.Open(colorsTempFile, FileMode.Open, FileAccess.Read, FileShare.None))
                using (BufferedStream bs = new BufferedStream(fs))
                using (BinaryReader binaryReader = new BinaryReader(bs))
                {
                    tempBytes = binaryReader.ReadBytes(pointCount * 4 * 3);
                }

                tempFloats = new float[pointCount * 3];

                // convert to float array, TODO no need if can output writeallbytes
                vectorPointer = GCHandle.Alloc(tempFloats, GCHandleType.Pinned);
                pV = vectorPointer.AddrOfPinnedObject();
                Marshal.Copy(tempBytes, 0, pV, pointCount * 4 * 3);
                vectorPointer.Free();

                // actual point randomization
                Tools.ShuffleXYZ(ref tempFloats);

                // create new file on top, seek didnt work?
                bsColorsV2 = new BufferedStream(new FileStream(colorsTempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.Read));
                writerColorsV2 = new BinaryWriter(bsColorsV2);

                // TODO why not use writeallbytes? check 2gb file limit then use that

                for (int i = 0; i < pointCount * 3; i++)
                {
                    writerColorsV2.Write(tempFloats[i]);
                }
            }
        }

        void IWriter.AddPoint(int index, float x, float y, float z, float r, float g, float b, bool hasIntensity, float i, bool hasTime, double time)
        {
            // skip points
            if (importSettings.skipPoints == true && (index % importSettings.skipEveryN == 0)) return;

            // keep points
            if (importSettings.keepPoints == true && (index % importSettings.keepEveryN != 0)) return;

            // get bounds
            if (x < cloudMinX) cloudMinX = x;
            if (x > cloudMaxX) cloudMaxX = x;
            if (y < cloudMinY) cloudMinY = y;
            if (y > cloudMaxY) cloudMaxY = y;
            if (z < cloudMinZ) cloudMinZ = z;
            if (z > cloudMaxZ) cloudMaxZ = z;

            importSettings.writer.WriteXYZ(x, y, z);
            importSettings.writer.WriteRGB(r, g, b);
        }

        void IWriter.Save(int fileIndex)
        {
            importSettings.writer.CreateHeader(pointCount);
            if (importSettings.randomize == true) importSettings.writer.Randomize();
            importSettings.writer.Close();
            importSettings.writer.Cleanup(fileIndex);
        }

        void IWriter.Cleanup(int fileIndex)
        {
            if (importSettings.packColors == true)
            {
                Log.WriteLine("Combining files: " + Path.GetFileName(headerTempFile) + "," + Path.GetFileName(pointsTempFile));
            }
            else
            {
                Log.WriteLine("Combining files: " + Path.GetFileName(headerTempFile) + "," + Path.GetFileName(pointsTempFile) + "," + Path.GetFileName(colorsTempFile));
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Log.WriteLine("Output: " + importSettings.outputFile);

            string jsonString = "{" +
            "\"event\": \"" + LogEvent.File + "\"," +
            "\"status\": \"" + LogStatus.Complete + "\"," +
            "\"path\": " + JsonSerializer.Serialize(importSettings.inputFiles[fileIndex]) + "," +
            "\"output\": " + JsonSerializer.Serialize(importSettings.outputFile) + "}";

            Log.WriteLine(jsonString, LogEvent.File);
            Console.ForegroundColor = ConsoleColor.White;

            var sep = '"';

            // fix slashes, forward slashes fail in command prompt too
            headerTempFile = headerTempFile.Replace("/", "\\");
            pointsTempFile = pointsTempFile.Replace("/", "\\");
            colorsTempFile = colorsTempFile.Replace("/", "\\");

            string outputFile = "";
            if (Directory.Exists(importSettings.outputFile)) // its output folder, take filename from source
            {
                outputFile = importSettings.outputFile + Path.GetFileNameWithoutExtension(importSettings.inputFiles[fileIndex]) + ".ucpc";
            }
            else // its not folder
            {
                // its filename with extension, use that
                if (Path.GetExtension(importSettings.outputFile).ToLower() == ".ucpc")
                {
                    outputFile = importSettings.outputFile;
                }
                else // its filename without extension
                {
                    outputFile = importSettings.outputFile + ".ucpc";
                }
            }

            // combine files using commandline binary append
            string args = "";
            if (importSettings.packColors == true)
            {
                args = "/C copy /b " + sep + headerTempFile + sep + "+" + sep + pointsTempFile + sep + " " + sep + outputFile + sep;
            }
            else // non packed
            {
                args = "/C copy /b " + sep + headerTempFile + sep + "+" + sep + pointsTempFile + sep + "+" + sep + colorsTempFile + sep + " " + sep + outputFile + sep;
            }
            Process proc = new Process();
            proc.StartInfo.FileName = "CMD.exe";
            proc.StartInfo.Arguments = args;
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.Start();
            proc.WaitForExit();

            Log.WriteLine("Deleting temporary files: " + Path.GetFileName(headerTempFile) + "," + Path.GetFileName(pointsTempFile) + "," + Path.GetFileName(colorsTempFile));
            if (File.Exists(headerTempFile)) File.Delete(headerTempFile);
            if (File.Exists(pointsTempFile)) File.Delete(pointsTempFile);
            if (File.Exists(colorsTempFile)) File.Delete(colorsTempFile);
        }

        void IWriter.Close()
        {
            writerPoints.Flush();
            bsPoints.Flush();
            writerPoints.Close();
            bsPoints.Dispose();
            writerColorsV2.Close();
            bsColorsV2.Dispose();
        }
    }
}
