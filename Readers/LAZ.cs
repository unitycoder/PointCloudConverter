// LAS/LAZ Reader https://github.com/shintadono/laszip.net
// This program uses theLAS/LAZ library for C#, which is licensed under the GNU Lesser General Public Library, version 2.1.
// LICENSE AGREEMENT(for LASzip.Net LiDAR compression)
// LASzip.Net is open-source and is licensed with the standard LGPL version 2.1 (see LICENSE file).
// This software is distributed WITHOUT ANY WARRANTY and without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// COPYRIGHT
// (c) 2007-2014, martin isenburg, rapidlasso - fast tools to catch reality
// (c) of C# port 2014-2017 by Shinta shintadono@googlemail.com

using PointCloudConverter.Structs;
using System;
using LASzip.Net;
using System.IO;
using PointCloudConverter.Structs.VariableLengthRecords;
using Free.Ports.LibGeoTiff;
using System.Text;
using Color = PointCloudConverter.Structs.Color;
using System.Xml.Linq;
using Windows.Data.Xml.Dom;

namespace PointCloudConverter.Readers
{
    public class LAZ : IReader, IDisposable
    {
        //laszip_dll lazReader = new laszip_dll();
        laszip lazReader = new laszip();

        bool compressedLAZ = false;
        //bool importRGB = true;
        //bool importIntensity = false;
        bool customIntensityRange = false;

        byte minClassification = 255;
        byte maxClassification = 0;
        byte minIntensity = 255;
        byte maxIntensity = 0;

        int? taskID;

        // add constructor
        public LAZ(int? _taskID)
        {
            //Log.WriteLine("*** LAZ reader created for task: " + _taskID);
            taskID = _taskID;
        }

        bool IReader.InitReader(ImportSettings importSettings, int fileIndex)
        {
            int res = 1;
            //try
            //{
            //Log.WriteLine("--------------------- initreader: " + fileIndex + " taskID: " + taskID);
            // TODO check errors
            var file = importSettings.inputFiles[fileIndex];
            //importRGB = importSettings.importRGB;
            //importIntensity = importSettings.importIntensity;
            customIntensityRange = importSettings.useCustomIntensityRange;

            minClassification = 255;
            maxClassification = 0;
            minIntensity = 255;
            maxIntensity = 0;

            res = lazReader.open_reader(file, out compressedLAZ); // 0 = ok, 1 = error
            //}
            //catch (Exception e)
            //{
            //    Log.WriteLine("Error in LAZ.InitReader: " + e.Message);
            //    throw;
            //}
            return (res == 0);
        }

        LasHeader IReader.GetMetaData(ImportSettings importSettings, int fileIndex)
        {
            var h = new LasHeader();

            h.FileName = importSettings.inputFiles[fileIndex];
            h.FileSourceID = lazReader.header.file_source_ID;
            h.GlobalEncoding = lazReader.header.global_encoding;
            h.ProjectID_GUID_data1 = lazReader.header.project_ID_GUID_data_1;
            h.ProjectID_GUID_data2 = lazReader.header.project_ID_GUID_data_2;
            h.ProjectID_GUID_data3 = lazReader.header.project_ID_GUID_data_3;
            h.ProjectID_GUID_data4 = lazReader.header.project_ID_GUID_data_4;
            h.VersionMajor = lazReader.header.version_major;
            h.VersionMinor = lazReader.header.version_minor;
            h.SystemIdentifier = System.Text.Encoding.UTF8.GetString(lazReader.header.system_identifier);
            h.SystemIdentifier = h.SystemIdentifier.Replace("\0", string.Empty);
            h.GeneratingSoftware = System.Text.Encoding.UTF8.GetString(lazReader.header.generating_software);
            h.GeneratingSoftware = h.GeneratingSoftware.Replace("\0", string.Empty);
            h.FileCreationDayOfYear = lazReader.header.file_creation_day;
            h.FileCreationYear = lazReader.header.file_creation_year;
            h.HeaderSize = lazReader.header.header_size;
            h.OffsetToPointData = lazReader.header.offset_to_point_data;
            h.NumberOfVariableLengthRecords = lazReader.header.number_of_variable_length_records;
            h.PointDataFormatID = lazReader.header.point_data_format;
            h.PointDataRecordLength = lazReader.header.point_data_record_length;
            h.NumberOfPointRecords = lazReader.header.number_of_point_records;
            h.NumberOfPointsByReturn = lazReader.header.number_of_points_by_return;
            h.XScaleFactor = lazReader.header.x_scale_factor;
            h.YScaleFactor = lazReader.header.y_scale_factor;
            h.ZScaleFactor = lazReader.header.z_scale_factor;
            h.XOffset = lazReader.header.x_offset;
            h.YOffset = lazReader.header.y_offset;
            h.ZOffset = lazReader.header.z_offset;
            h.MinX = lazReader.header.min_x;
            h.MaxX = lazReader.header.max_x;
            h.MinY = lazReader.header.min_y;
            h.MaxY = lazReader.header.max_y;
            h.MinZ = lazReader.header.min_z;
            h.MaxZ = lazReader.header.max_z;

            if (importSettings.importClassification)
            {
                h.MinClassification = minClassification;
                h.MaxClassification = maxClassification;
            }

            if (importSettings.importIntensity)
            {
                h.MinIntensity = minIntensity;
                h.MaxIntensity = maxIntensity;
            }

            if (h.NumberOfVariableLengthRecords > 0)
            {
                h.VariableLengthRecords = new System.Collections.Generic.List<LasVariableLengthRecord>();
                for (int i = 0; i < h.NumberOfVariableLengthRecords; i++)
                {
                    var vlr = new LasVariableLengthRecord();
                    vlr.Reserved = lazReader.header.vlrs[i].reserved;
                    vlr.UserID = System.Text.Encoding.UTF8.GetString(lazReader.header.vlrs[i].user_id);
                    vlr.UserID = vlr.UserID.Replace("\0", string.Empty);
                    vlr.RecordID = lazReader.header.vlrs[i].record_id;
                    vlr.RecordLengthAfterHeader = lazReader.header.vlrs[i].record_length_after_header;
                    vlr.Description = System.Text.Encoding.UTF8.GetString(lazReader.header.vlrs[i].description);
                    vlr.Description = vlr.Description.Replace("\0", string.Empty);

                    // Get WKT (Well Known Text String)
                    if (vlr.RecordID == 2112)
                    {
                        string wkt = Encoding.ASCII.GetString(lazReader.header.vlrs[i].data);
                        Console.WriteLine("WKT string = " + wkt);
                        h.WKT = wkt;
                    }

                    // get GeoKeyDirectoryTag
                    if (vlr.RecordID == 34735)
                    {
                        vlr.GeoKeys = new System.Collections.Generic.List<sGeoKeys>();
                        var g = ParseGeoKeysFromByteArray(lazReader.header.vlrs[i].data);
                        var gk = new sGeoKeys
                        {
                            // TODO parse human readable values from geotiff list
                            KeyDirectoryVersion = g.wKeyDirectoryVersion,
                            KeyRevision = g.wKeyRevision,
                            MinorRevision = g.wMinorRevision,
                            NumberOfKeys = g.wNumberOfKeys,
                            KeyEntries = new System.Collections.Generic.List<sKeyEntry>()
                        };

                        for (int k = 0; k < gk.NumberOfKeys; k++)
                        {
                            var newEntry = new sKeyEntry
                            {
                                KeyID = g.pKey[k].wKeyID,
                                KeyIDString = Enum.GetName(typeof(GeoTiffKeys), g.pKey[k].wKeyID),
                                TIFFTagLocation = g.pKey[k].wTIFFTagLocation,
                                Count = g.pKey[k].wCount,
                                Value_Offset = g.pKey[k].wValue_Offset,
                                Value_OffsetString = Enum.GetName(typeof(GeoTiffKeys), g.pKey[k].wValue_Offset)
                            };

                            if (newEntry.KeyID == 3072)
                            {
                                h.ProjectionID = newEntry.Value_Offset;
                                h.Projection = newEntry.Value_OffsetString;
                            }
                            gk.KeyEntries.Add(newEntry);

                            //gk.KeyEntries.Add(new sKeyEntry
                            //{
                            //    KeyID = g.pKey[k].wKeyID, // Defined key ID for each piece of GeoTIFF data. IDs contained in the GeoTIFF specification
                            //    KeyIDString = Enum.GetName(typeof(GeoTiffKeys), g.pKey[k].wKeyID),
                            //    TIFFTagLocation = g.pKey[k].wTIFFTagLocation, // 0 =wValue_Offset field as an unsigned short, 34736 means the data is located at index wValue_Offset of the GeoDoubleParamsTag record, 34767 means the data is located at index wValue_Offset of the GeoAsciiParamsTag record
                            //    Count = g.pKey[k].wCount, // Number of characters in string for values of GeoAsciiParamsTag, otherwise is 1
                            //    Value_Offset = g.pKey[k].wValue_Offset, // Contents vary depending on value for wTIFFTagLocation above
                            //    Value_OffsetString = Enum.GetName(typeof(GeoTiffKeys), g.pKey[k].wValue_Offset)
                            //});
                        }
                        vlr.GeoKeys.Add(gk);
                    }

                    // get GeoAsciiParamsTag
                    if (vlr.RecordID == 34737)
                    {
                        vlr.GeoAsciiParamsTag = System.Text.Encoding.UTF8.GetString(lazReader.header.vlrs[i].data);
                        vlr.GeoAsciiParamsTag = vlr.GeoAsciiParamsTag.Replace("\0", string.Empty);
                    }

                    h.VariableLengthRecords.Add(vlr);
                }
            }

            // additional data for LAS 1.3/1.4

            // LAS 1.3 and higher: waveform data packet record pointer.
            if (h.VersionMajor > 1 || (h.VersionMajor == 1 && h.VersionMinor >= 3))
            {
                h.StartOfWaveformDataPacketRecord = lazReader.header.start_of_waveform_data_packet_record;
            }

            // LAS 1.4 and higher: extended VLRs and extended point record counts.
            if (h.VersionMajor > 1 || (h.VersionMajor == 1 && h.VersionMinor >= 4))
            {
                h.StartOfFirstExtendedVariableLengthRecord = lazReader.header.start_of_first_extended_variable_length_record;
                h.NumberOfExtendedVariableLengthRecords = lazReader.header.number_of_extended_variable_length_records;
                h.ExtendedNumberOfPointRecords = lazReader.header.extended_number_of_point_records;
                h.ExtendedNumberOfPointsByReturn = lazReader.header.extended_number_of_points_by_return;
            }

            // optional user data in header
            if (lazReader.header.user_data_in_header_size > 0)
            {
                h.UserDataInHeader = lazReader.header.user_data_in_header;
            }

            // optional user data after header
            if (lazReader.header.user_data_after_header_size > 0)
            {
                h.UserDataAfterHeader = lazReader.header.user_data_after_header;
            }

            return h;
        }


        public GeoKeys ParseGeoKeysFromByteArray(byte[] byteArray)
        {
            GeoKeys geoKeys = new GeoKeys
            {
                wKeyDirectoryVersion = BitConverter.ToUInt16(byteArray, 0),
                wKeyRevision = BitConverter.ToUInt16(byteArray, 2),
                wMinorRevision = BitConverter.ToUInt16(byteArray, 4),
                wNumberOfKeys = BitConverter.ToUInt16(byteArray, 6)
            };

            geoKeys.pKey = new KeyEntry[geoKeys.wNumberOfKeys];
            int offset = 8;  // Initial offset after reading wNumberOfKeys.

            for (int i = 0; i < geoKeys.wNumberOfKeys; i++)
            {
                geoKeys.pKey[i] = new KeyEntry
                {
                    wKeyID = BitConverter.ToUInt16(byteArray, offset),
                    wTIFFTagLocation = BitConverter.ToUInt16(byteArray, offset + 2),
                    wCount = BitConverter.ToUInt16(byteArray, offset + 4),
                    wValue_Offset = BitConverter.ToUInt16(byteArray, offset + 6)
                };
                offset += 8;  // Move to the next KeyEntry.
            }

            return geoKeys;
        }

        public void PrintGeoKeys(GeoKeys geoKeys)
        {
            Console.WriteLine($"Key Directory Version: {geoKeys.wKeyDirectoryVersion}");
            Console.WriteLine($"Key Revision: {geoKeys.wKeyRevision}");
            Console.WriteLine($"Minor Revision: {geoKeys.wMinorRevision}");
            Console.WriteLine($"Number of Keys: {geoKeys.wNumberOfKeys}");

            for (int i = 0; i < geoKeys.wNumberOfKeys; i++)
            {
                Console.WriteLine($"Key {i + 1}:");
                Console.WriteLine($"  Key ID: {geoKeys.pKey[i].wKeyID}");
                Console.WriteLine($"  TIFF Tag Location: {geoKeys.pKey[i].wTIFFTagLocation}");
                Console.WriteLine($"  Count: {geoKeys.pKey[i].wCount}");
                Console.WriteLine($"  Value Offset: {geoKeys.pKey[i].wValue_Offset}");
            }
        }

        public struct KeyEntry
        {
            public ushort wKeyID;
            public ushort wTIFFTagLocation;
            public ushort wCount;
            public ushort wValue_Offset;
        }

        public struct GeoKeys
        {
            public ushort wKeyDirectoryVersion;
            public ushort wKeyRevision;
            public ushort wMinorRevision;
            public ushort wNumberOfKeys;
            public KeyEntry[] pKey;
        }

        public static GeoKeys ReadGeoKeys(string filePath, long offset)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                // Seek to the specified offset in the file
                fs.Seek(offset, SeekOrigin.Begin);

                // Read the GeoKeys structure
                GeoKeys geoKeys = new GeoKeys
                {
                    wKeyDirectoryVersion = reader.ReadUInt16(),
                    wKeyRevision = reader.ReadUInt16(),
                    wMinorRevision = reader.ReadUInt16(),
                    wNumberOfKeys = reader.ReadUInt16()
                };

                // Initialize the array of key entries
                geoKeys.pKey = new KeyEntry[geoKeys.wNumberOfKeys];

                // Read each key entry
                for (int i = 0; i < geoKeys.wNumberOfKeys; i++)
                {
                    geoKeys.pKey[i] = new KeyEntry
                    {
                        wKeyID = reader.ReadUInt16(),
                        wTIFFTagLocation = reader.ReadUInt16(),
                        wCount = reader.ReadUInt16(),
                        wValue_Offset = reader.ReadUInt16()
                    };
                }

                return geoKeys;
            }
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
            // get gps week offset from header

            long count = 0;
            lazReader.get_point_count(out count);
            // check alternative point counts
            if (count == 0) count = (int)lazReader.header.extended_number_of_point_records;
            if (count == 0) count = lazReader.header.number_of_point_records;
            return (int)count;
        }

        Color IReader.GetRGB()
        {
            var c = new Color();

            // get point reference
            var p = lazReader.point;

            if (p.rgb[0] > 255 || p.rgb[1] > 255 || p.rgb[2] > 255)
            {
                c.r = Tools.LUT255[(byte)(p.rgb[0] / 256f)];
                c.g = Tools.LUT255[(byte)(p.rgb[1] / 256f)];
                c.b = Tools.LUT255[(byte)(p.rgb[2] / 256f)];
            }
            else // Values are within the 0-255 range
            {
                c.r = Tools.LUT255[(byte)(p.rgb[0])];
                c.g = Tools.LUT255[(byte)(p.rgb[1])];
                c.b = Tools.LUT255[(byte)(p.rgb[2])];
            }

            return c;
        }

        byte IReader.GetIntensity()
        {
            //var c = new Color();

            // get point reference
            var p = lazReader.point;

            byte i = 0;
            if (customIntensityRange == true) // NOTE now only supports 65535 as custom range
            {
                //i = Tools.LUT255[(byte)(p.intensity / 255f)];
                i = (byte)(p.intensity / 255f);
            }
            else
            {
                //i = Tools.LUT255[(byte)(p.intensity)];
                i = (byte)(p.intensity);
            }
            //c.r = i;
            //c.g = i;
            //c.b = i;

            // get min and max
            if (i < minIntensity) minIntensity = i;
            if (i > maxIntensity) maxIntensity = i;


            return i;
        }

        byte IReader.GetClassification()
        {
            var p = lazReader.point;
            // now reads both, we dont know which one is enabled?
            byte classification = p.classification;
            byte extended = p.extended_classification;
            // Choose extended if it's valid and not equal to default "unclassified"
            byte finalClassification = (extended > 0 && extended != classification) ? extended : classification;

            // get min and max
            if (finalClassification < minClassification) minClassification = finalClassification;
            if (finalClassification > maxClassification) maxClassification = finalClassification;

            return finalClassification;
        }

        Float3 IReader.GetXYZ()
        {
            var f = new Float3();
            f.hasError = false;

            // Read point
            int err = lazReader.read_point();

            // check for received errors
            //var err = lazReader.get_error();
            //if (err == null)
            if (err != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to read until end of file?");
                Console.WriteLine("ErrorCode: " + err);
                Console.ForegroundColor = ConsoleColor.White;
                f.hasError = true;
            }

            // Get precision coordinates
            var coordArray = new double[3];
            lazReader.get_coordinates(coordArray);
            f.x = coordArray[0];
            f.y = coordArray[1];
            f.z = coordArray[2];

            return f;
        }

        double IReader.GetTime()
        {
            // NOTE this is probably the "raw" time value, not adjusted/corrected based on GPS week data
            return lazReader.point.gps_time;
        }

        //(byte, byte) IReader.GetClassificationRange()
        //{
        //    return (minClassification, maxClassification);
        //}

        //(byte, byte) IReader.GetIntensityRange()
        //{
        //    return (minIntensity, maxIntensity);
        //}

        void IReader.Close()
        {
            lazReader.close_reader();
        }

        public void Dispose()
        {
            //Log.WriteLine("Memory used: " + GC.GetTotalMemory(false));
            //Log.WriteLine("*** LAZ reader disposed for task: " + taskID);
            Dispose(true);
            GC.SuppressFinalize(this);
            GC.Collect();
            //Log.WriteLine("Memory used: " + GC.GetTotalMemory(false));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lazReader = null;
            }
        }

        ~LAZ()
        {
            Dispose(false);
        }
    } // class
} // namespace
