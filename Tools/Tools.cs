using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace PointCloudConverter
{
    public static class Tools
    {
        public static readonly int seed = Guid.NewGuid().GetHashCode();
        public static Random rnd = new Random(seed);
        public static void ResetRandom()
        {
            rnd = new Random(seed);
        }

        // force comma as decimal separator
        public static void ForceDotCultureSeparator()
        {
            string CultureName = Thread.CurrentThread.CurrentCulture.Name;
            CultureInfo ci = new CultureInfo(CultureName);
            if (ci.NumberFormat.NumberDecimalSeparator != ".")
            {
                ci.NumberFormat.NumberDecimalSeparator = ".";
                Thread.CurrentThread.CurrentCulture = ci;
            }
        }

        // this fixes dll reading from lib folder (so they dont clutter root folder) and no need exe.config file https://stackoverflow.com/a/10600034/5452781
        public static void FixDLLFoldersAndConfig(string rootFolder)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, bArgs) =>
            {
                string assemblyName = new AssemblyName(bArgs.Name).Name;
                if (assemblyName.EndsWith(".resources")) return null;
                string dllName = assemblyName + ".dll";
                return Assembly.LoadFrom(Path.Combine(rootFolder, "lib" + Path.DirectorySeparatorChar + dllName));
            };
        }


        // https://stackoverflow.com/a/48000498/5452781
        public static string HumanReadableCount(long num)
        {
            if (num > 999999999 || num < -999999999)
            {
                return num.ToString("0,,,.###B", CultureInfo.InvariantCulture);
            }
            else
            if (num > 999999 || num < -999999)
            {
                return num.ToString("0,,.##M", CultureInfo.InvariantCulture);
            }
            else
            if (num > 999 || num < -999)
            {
                return num.ToString("0,.#K", CultureInfo.InvariantCulture);
            }
            else
            {
                return num.ToString(CultureInfo.InvariantCulture);
            }
        }

        // https://stackoverflow.com/a/4975942/5452781
        public static String HumanReadableFileSize(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }

        public static float SuperPacker(float coord, float color, float range)
        {
            float truncated = (float)Math.Truncate(color * range);
            return truncated + coord;
        }

        public static void Shuffle<T>(Random rng, ref List<T> array1, ref List<T> array2, ref List<T> array3, ref List<T> arrayR, ref List<T> arrayG, ref List<T> arrayB)
        {
            int index = array1.Count;
            while (index > 1)
            {
                int rnd = rng.Next(index--);

                T temp = array1[index];
                array1[index] = array1[rnd];
                array1[rnd] = temp;

                T temp2 = array2[index];
                array2[index] = array2[rnd];
                array2[rnd] = temp2;

                T temp3 = array3[index];
                array3[index] = array3[rnd];
                array3[rnd] = temp3;

                T tempR = arrayR[index];
                arrayR[index] = arrayR[rnd];
                arrayR[rnd] = tempR;

                T tempG = arrayG[index];
                arrayG[index] = arrayG[rnd];
                arrayG[rnd] = tempG;

                T tempB = arrayB[index];
                arrayB[index] = arrayB[rnd];
                arrayB[rnd] = tempB;
            }
        }

        // https://stackoverflow.com/a/110570/5452781
        public static void ShuffleXYZ<T>(Random rng, ref T[] array1)
        {
            int n = array1.Length;
            int maxVal = array1.Length / 3; // xyz key

            while (n > 3)
            {
                int k = rng.Next(maxVal) * 3; // multiples of 3 only
                n -= 3;

                T tempX = array1[n];
                array1[n] = array1[k];
                array1[k] = tempX;

                T tempY = array1[n + 1];
                array1[n + 1] = array1[k + 1];
                array1[k + 1] = tempY;

                T tempZ = array1[n + 2];
                array1[n + 2] = array1[k + 2];
                array1[k + 2] = tempZ;
            }
        }

        public static int ParseInt(string s)
        {
            int f = 0;
            // TODO add invariant culture
            int.TryParse(s, out f);
            return f;
        }

        public static float ParseFloat(string s)
        {
            float f = 0;
            // TODO add invariant culture
            float.TryParse(s, out f);
            return f;
        }

        public static void PrintHelpAndExit(char argSeparator, bool waitEnter = false)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Converts LAS/LAZ pointcloud files into UnityCoder PointCloud Viewer for Unity (v2 .ucpc & v3 .pct formats)");
            Console.WriteLine("More info https://github.com/unitycoder/UnityPointCloudViewer");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("");
            Console.WriteLine("--- Required Parameters ---");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("-input" + argSeparator + "yourfile.laz\tInput file with relative or absolute filepath (or folder with multiple files)");
            // TODO get list here for supported formats.. from interfaces, or from enum
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("");
            Console.WriteLine("--- Optional parameters ---");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("-importformat" + argSeparator + "laz\tSupported import formats: LAZ, LAS)\tDefault is LAS/LAZ");
            Console.WriteLine("-exportformat" + argSeparator + "ucpc\tSupported export formats: UCPC (v2), PCROOT (v3))\tDefault is UCPC (v2)");
            Console.WriteLine("-output" + argSeparator + "yourfile.ucpc\t(Default is same folder as input file. For v3 you dont need to set file extension)");
            Console.WriteLine("-offset" + argSeparator + "true or false\tAuto-offsets cloud near 0,0,0 by using the first point as offset value\tDefault is true");
            Console.WriteLine("-gridsize" + argSeparator + "5\t\tGridsize in meters, splits cloud into tiles with this size. v3 only!\tDefault is 5, minimum is 0.1 (Note: values below 1 are not really tested)");
            Console.WriteLine("-minpoints" + argSeparator + "1000\t\tIf tile has less points than this value, its discarded. Good for removing straypoints. v3 only!\tDefault is 1000");
            Console.WriteLine("-scale" + argSeparator + "0.1\t\tScale XYZ values (You need meters inside Unity)\tDefault is off");
            Console.WriteLine("-swap" + argSeparator + "true or false\tSwaps Z and Y values, since unity Y is up\tDefault is true");
            Console.WriteLine("-pack" + argSeparator + "true or false\tPacks color values, improves performance in viewer (but can cause lower precision positions and colors). Requires using special packed material&shader in viewer\tDefault is false");
            Console.WriteLine("-packmagic" + argSeparator + "64\t\tOptional packing adjustment MagicInteger. Increase this value is you have large tiles and notice precision issues with packed data\tDefault is 64");
            Console.WriteLine("-limit" + argSeparator + "10000\t\tLoad only this many points (good for testing settings first)\tDefault is off");
            Console.WriteLine("-skip" + argSeparator + "0\t\t\tSkip every Nth point (For reducing point count)\tDefault is off");
            Console.WriteLine("-keep" + argSeparator + "0\t\t\tKeep only every Nth point (For reducing point count)\tDefault is off");
            Console.WriteLine("-maxfiles" + argSeparator + "10\t\t\tFor batch processing, parse only this many files (good for testing with few files first)\tDefault is parse all found files");
            // TODO Console.WriteLine("-decimate" + separator + "50\t\t\tRemoves 50% of the points (by skipping every x point)\tDefault is off");
            //Console.WriteLine("-version" + argSeparator + "2\t\t2=v2 .ucpc, 3=v3 .pcroot tiles\tDefault is 2");
            Console.WriteLine("-randomize" + argSeparator + "true\t\tRandomize point indexes, to use Dynamic resolution\tDefault is true (Always enabled for v3)");
            Console.WriteLine("");
            Console.WriteLine("? /? -? help -help /help");
            Console.ForegroundColor = ConsoleColor.White;
            if (waitEnter == true) Console.ReadLine();
            Environment.Exit(0);
        }

        // lookuptable: converts byte 0-255 into float 0-1f
        public static float[] LUT255 = new float[] { 0f, 0.00392156862745098f, 0.00784313725490196f, 0.011764705882352941f, 0.01568627450980392f, 0.0196078431372549f, 0.023529411764705882f, 0.027450980392156862f, 0.03137254901960784f, 0.03529411764705882f, 0.0392156862745098f, 0.043137254901960784f, 0.047058823529411764f, 0.050980392156862744f, 0.054901960784313725f, 0.058823529411764705f, 0.06274509803921569f, 0.06666666666666667f, 0.07058823529411765f, 0.07450980392156863f, 0.0784313725490196f, 0.08235294117647059f, 0.08627450980392157f, 0.09019607843137255f, 0.09411764705882353f, 0.09803921568627451f, 0.10196078431372549f, 0.10588235294117647f, 0.10980392156862745f, 0.11372549019607843f, 0.11764705882352941f, 0.12156862745098039f, 0.12549019607843137f, 0.12941176470588237f, 0.13333333333333333f, 0.13725490196078433f, 0.1411764705882353f, 0.1450980392156863f, 0.14901960784313725f, 0.15294117647058825f, 0.1568627450980392f, 0.1607843137254902f, 0.16470588235294117f, 0.16862745098039217f, 0.17254901960784313f, 0.17647058823529413f, 0.1803921568627451f, 0.1843137254901961f, 0.18823529411764706f, 0.19215686274509805f, 0.19607843137254902f, 0.2f, 0.20392156862745098f, 0.20784313725490197f, 0.21176470588235294f, 0.21568627450980393f, 0.2196078431372549f, 0.2235294117647059f, 0.22745098039215686f, 0.23137254901960785f, 0.23529411764705882f, 0.23921568627450981f, 0.24313725490196078f, 0.24705882352941178f, 0.25098039215686274f, 0.2549019607843137f, 0.25882352941176473f, 0.2627450980392157f, 0.26666666666666666f, 0.27058823529411763f, 0.27450980392156865f, 0.2784313725490196f, 0.2823529411764706f, 0.28627450980392155f, 0.2901960784313726f, 0.29411764705882354f, 0.2980392156862745f, 0.30196078431372547f, 0.3058823529411765f, 0.30980392156862746f, 0.3137254901960784f, 0.3176470588235294f, 0.3215686274509804f, 0.3254901960784314f, 0.32941176470588235f, 0.3333333333333333f, 0.33725490196078434f, 0.3411764705882353f, 0.34509803921568627f, 0.34901960784313724f, 0.35294117647058826f, 0.3568627450980392f, 0.3607843137254902f, 0.36470588235294116f, 0.3686274509803922f, 0.37254901960784315f, 0.3764705882352941f, 0.3803921568627451f, 0.3843137254901961f, 0.38823529411764707f, 0.39215686274509803f, 0.396078431372549f, 0.4f, 0.403921568627451f, 0.40784313725490196f, 0.4117647058823529f, 0.41568627450980394f, 0.4196078431372549f, 0.4235294117647059f, 0.42745098039215684f, 0.43137254901960786f, 0.43529411764705883f, 0.4392156862745098f, 0.44313725490196076f, 0.4470588235294118f, 0.45098039215686275f, 0.4549019607843137f, 0.4588235294117647f, 0.4627450980392157f, 0.4666666666666667f, 0.47058823529411764f, 0.4745098039215686f, 0.47843137254901963f, 0.4823529411764706f, 0.48627450980392156f, 0.49019607843137253f, 0.49411764705882355f, 0.4980392156862745f, 0.5019607843137255f, 0.5058823529411764f, 0.5098039215686274f, 0.5137254901960784f, 0.5176470588235295f, 0.5215686274509804f, 0.5254901960784314f, 0.5294117647058824f, 0.5333333333333333f, 0.5372549019607843f, 0.5411764705882353f, 0.5450980392156862f, 0.5490196078431373f, 0.5529411764705883f, 0.5568627450980392f, 0.5607843137254902f, 0.5647058823529412f, 0.5686274509803921f, 0.5725490196078431f, 0.5764705882352941f, 0.5803921568627451f, 0.5843137254901961f, 0.5882352941176471f, 0.592156862745098f, 0.596078431372549f, 0.6f, 0.6039215686274509f, 0.6078431372549019f, 0.611764705882353f, 0.615686274509804f, 0.6196078431372549f, 0.6235294117647059f, 0.6274509803921569f, 0.6313725490196078f, 0.6352941176470588f, 0.6392156862745098f, 0.6431372549019608f, 0.6470588235294118f, 0.6509803921568628f, 0.6549019607843137f, 0.6588235294117647f, 0.6627450980392157f, 0.6666666666666666f, 0.6705882352941176f, 0.6745098039215687f, 0.6784313725490196f, 0.6823529411764706f, 0.6862745098039216f, 0.6901960784313725f, 0.6941176470588235f, 0.6980392156862745f, 0.7019607843137254f, 0.7058823529411765f, 0.7098039215686275f, 0.7137254901960784f, 0.7176470588235294f, 0.7215686274509804f, 0.7254901960784313f, 0.7294117647058823f, 0.7333333333333333f, 0.7372549019607844f, 0.7411764705882353f, 0.7450980392156863f, 0.7490196078431373f, 0.7529411764705882f, 0.7568627450980392f, 0.7607843137254902f, 0.7647058823529411f, 0.7686274509803922f, 0.7725490196078432f, 0.7764705882352941f, 0.7803921568627451f, 0.7843137254901961f, 0.788235294117647f, 0.792156862745098f, 0.796078431372549f, 0.8f, 0.803921568627451f, 0.807843137254902f, 0.8117647058823529f, 0.8156862745098039f, 0.8196078431372549f, 0.8235294117647058f, 0.8274509803921568f, 0.8313725490196079f, 0.8352941176470589f, 0.8392156862745098f, 0.8431372549019608f, 0.8470588235294118f, 0.8509803921568627f, 0.8549019607843137f, 0.8588235294117647f, 0.8627450980392157f, 0.8666666666666667f, 0.8705882352941177f, 0.8745098039215686f, 0.8784313725490196f, 0.8823529411764706f, 0.8862745098039215f, 0.8901960784313725f, 0.8941176470588236f, 0.8980392156862745f, 0.9019607843137255f, 0.9058823529411765f, 0.9098039215686274f, 0.9137254901960784f, 0.9176470588235294f, 0.9215686274509803f, 0.9254901960784314f, 0.9294117647058824f, 0.9333333333333333f, 0.9372549019607843f, 0.9411764705882353f, 0.9450980392156862f, 0.9490196078431372f, 0.9529411764705882f, 0.9568627450980393f, 0.9607843137254902f, 0.9647058823529412f, 0.9686274509803922f, 0.9725490196078431f, 0.9764705882352941f, 0.9803921568627451f, 0.984313725490196f, 0.9882352941176471f, 0.9921568627450981f, 0.996078431372549f, 1f };
    }
}
