// LAS/LAZ Reader https://github.com/shintadono/laszip.net
// This program uses theLAS/LAZ library for C#, which is licensed under the GNU Lesser General Public Library, version 2.1.
// LICENSE AGREEMENT(for LASzip.Net LiDAR compression)
// LASzip.Net is open-source and is licensed with the standard LGPL version 2.1 (see LICENSE file).
// This software is distributed WITHOUT ANY WARRANTY and without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// COPYRIGHT
// (c) 2007-2014, martin isenburg, rapidlasso - fast tools to catch reality
// (c) of C# port 2014-2017 by Shinta shintadono@googlemail.com

using PointCloudConverter.Structs;
using laszip.net;
using System;

namespace PointCloudConverter.Readers
{
    public class LAZ : IReader
    {
        laszip_dll lazReader = new laszip_dll();
        bool compressed = false;
        bool importRGB = true;
        bool customIntensityRange = false;

        bool IReader.InitReader(ImportSettings importSettings, int fileIndex)
        {
            // TODO check errors
            var file = importSettings.inputFiles[fileIndex];
            importRGB = importSettings.importRGB;
            //importIntensity = importSettings.readIntensity;
            customIntensityRange = importSettings.useCustomIntensityRange;
            lazReader.laszip_open_reader(file, ref compressed);
            return true;
        }

        Bounds IReader.GetBounds()
        {
            var b = new Bounds();

            // get original bounds from file
            b.minX = (float)lazReader.header.min_x;
            b.maxX = (float)lazReader.header.max_x;
            b.minY = (float)lazReader.header.min_y;
            b.maxY = (float)lazReader.header.max_y;
            b.minZ = (float)lazReader.header.min_z;
            b.maxZ = (float)lazReader.header.max_z;

            return b;
        }

        int IReader.GetPointCount()
        {
            return (int)lazReader.header.number_of_point_records;
        }


        Color IReader.GetRGB()
        {
            var c = new Color();

            // get point reference
            var p = lazReader.point;

            if (importRGB == true)
            {
                // try to detect if colors are outside 0-255 range? TODO just check value?
                if (p.rgb[0].ToString("X").Length > 2)
                {
                    c.r = Tools.LUT255[(byte)(p.rgb[0] / 256f)];
                    c.g = Tools.LUT255[(byte)(p.rgb[1] / 256f)];
                    c.b = Tools.LUT255[(byte)(p.rgb[2] / 256f)];
                }
                else // its 0-255
                {
                    c.r = Tools.LUT255[(byte)(p.rgb[0])];
                    c.g = Tools.LUT255[(byte)(p.rgb[1])];
                    c.b = Tools.LUT255[(byte)(p.rgb[2])];
                }
            }
            else // use intensity
            {
                float i = 0;
                if (customIntensityRange) // NOTE now only supports 65535 as custom range
                {
                    i = Tools.LUT255[(byte)(p.intensity / 255f)];
                }
                else
                {
                    i = Tools.LUT255[(byte)(p.intensity)];
                }
                c.r = i;
                c.g = i;
                c.b = i;
            }

            return c;
        }

        Float3 IReader.GetXYZ()
        {
            var f = new Float3();
            f.hasError = false;

            // Read point
            lazReader.laszip_read_point();

            // check for received errors
            var err = lazReader.laszip_get_error();
            if (err != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to read until end of file, partial data is kept.");
                Console.WriteLine("ErrorCode: " + err);
                Console.ForegroundColor = ConsoleColor.White;
                f.hasError = true;
            }

            // Get precision coordinates
            var coordArray = new double[3];
            lazReader.laszip_get_coordinates(coordArray);
            f.x = coordArray[0];
            f.y = coordArray[1];
            f.z = coordArray[2];

            return f;
        }

        void IReader.Close()
        {
            lazReader.laszip_close_reader();
        }
    } // class
} // namespace
