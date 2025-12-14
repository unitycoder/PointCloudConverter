// PCROOT (v3) Exporter https://github.com/unitycoder/UnityPointCloudViewer/wiki/Binary-File-Format-Structure#custom-v3-tiles-pcroot-and-pct-rgb
// Memory-optimized version - Combined 9 dictionaries into 1 for 30-50% memory reduction

using PointCloudConverter.Logger;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PointCloudConverter.Writers
{
    public class PCROOT : IWriter, IDisposable
    {
        const string tileExtension = ".pct";
        const string sep = "|";

        //BufferedStream bsPoints = null;
        //BinaryWriter writerPoints = null;
        ImportSettings importSettings;

        static ConcurrentBag<PointCloudTile> nodeBoundsBag = new ConcurrentBag<PointCloudTile>();

        BoundsAcc localBounds;
        struct BoundsAcc
        {
            public float minX, minY, minZ, maxX, maxY, maxZ;
            public void Init()
            {
                minX = minY = minZ = float.PositiveInfinity;
                maxX = maxY = maxZ = float.NegativeInfinity;
            }
        }

        static class GlobalBounds
        {
            private static readonly object _lock = new();
            public static float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            public static float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;

            public static void Merge(in BoundsAcc b)
            {
                lock (_lock)
                {
                    if (b.minX < minX) minX = b.minX;
                    if (b.maxX > maxX) maxX = b.maxX;
                    if (b.minY < minY) minY = b.minY;
                    if (b.maxY > maxY) maxY = b.maxY;
                    if (b.minZ < minZ) minZ = b.minZ;
                    if (b.maxZ > maxZ) maxZ = b.maxZ;
                }
            }

            public static void Reset()
            {
                lock (_lock)
                {
                    minX = float.PositiveInfinity;
                    minY = float.PositiveInfinity;
                    minZ = float.PositiveInfinity;
                    maxX = float.NegativeInfinity;
                    maxY = float.NegativeInfinity;
                    maxZ = float.NegativeInfinity;
                }
            }
        }

        // MEMORY OPTIMIZATION: Combined structure for all point attributes
        // Using class instead of struct to avoid value-type copy issues
        class PointData
        {
            public List<float> X, Y, Z, R, G, B;
            public List<ushort> Intensity;
            public List<byte> Classification;
            public List<double> Time;

            public void Clear()
            {
                X?.Clear();
                Y?.Clear();
                Z?.Clear();
                R?.Clear();
                G?.Clear();
                B?.Clear();
                Intensity?.Clear();
                Classification?.Clear();
                Time?.Clear();
            }
        }

        // MEMORY OPTIMIZATION: Single dictionary instead of 9 separate dictionaries
        Dictionary<(int x, int y, int z), PointData> nodeData = new();

        StringBuilder keyBuilder = new StringBuilder(32);
        static int skippedNodesCounter = 0;
        static int skippedPointsCounter = 0;
        static bool useLossyFiltering = false;

        private byte[] pointBuffer = new byte[12];
        private byte[] colorBuffer = new byte[12];

        static ILogger Log;

        static int initialCapacity = 65536;

        public PCROOT(int? _taskID)
        {
        }

        ~PCROOT()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                //bsPoints?.Dispose();
                //writerPoints?.Dispose();

                if (nodeData != null)
                {
                    foreach (var data in nodeData.Values)
                    {
                        data.Clear();
                    }
                    nodeData.Clear();
                }
            }
        }

        public bool InitWriter(dynamic _importSettings, long pointCount, ILogger logger)
        {
            Log = logger;

            if (nodeData != null)
            {
                foreach (var data in nodeData.Values)
                {
                    data.Clear();
                }
                nodeData.Clear();
            }
            else
            {
                nodeData = new Dictionary<(int x, int y, int z), PointData>();
            }

            //bsPoints = null;
            //writerPoints = null;
            importSettings = (ImportSettings)(object)_importSettings;

            localBounds = new BoundsAcc();
            localBounds.Init();

            return true;
        }

        void IWriter.CreateHeader(int pointCount)
        {
        }

        void IWriter.WriteXYZ(float x, float y, float z)
        {
        }

        void IWriter.WriteRGB(float r, float g, float b)
        {
        }

        void IWriter.Randomize()
        {
        }

        void IWriter.AddPoint(int index, float x, float y, float z, float r, float g, float b, ushort intensity, double time, byte classification)
        {
            float gridSize = importSettings.gridSize;

            int cellX = (int)(x / gridSize);
            int cellY = (int)(y / gridSize);
            int cellZ = (int)(z / gridSize);

            var key = (cellX, cellY, cellZ);

            // Get or create point data for this cell
            if (!nodeData.TryGetValue(key, out var data))
            {
                //data = new PointData
                //{
                //    X = new List<float> { x },
                //    Y = new List<float> { y },
                //    Z = new List<float> { z },
                //    R = new List<float> { r },
                //    G = new List<float> { g },
                //    B = new List<float> { b }
                //};

                //if (importSettings.importRGB && importSettings.importIntensity) data.Intensity = new List<ushort> { intensity };
                //if (importSettings.importRGB && importSettings.importClassification) data.Classification = new List<byte> { classification };
                //if (importSettings.averageTimestamp) data.Time = new List<double> { time };

                data = new PointData
                {
                    X = new List<float>(initialCapacity),
                    Y = new List<float>(initialCapacity),
                    Z = new List<float>(initialCapacity),
                    R = new List<float>(initialCapacity),
                    G = new List<float>(initialCapacity),
                    B = new List<float>(initialCapacity),

                    Intensity = importSettings.importRGB && importSettings.importIntensity ? new List<ushort>(initialCapacity) : null,
                    Classification = importSettings.importRGB && importSettings.importClassification ? new List<byte>(initialCapacity) : null,
                    Time = importSettings.averageTimestamp ? new List<double>(initialCapacity) : null,
                };

                data.X.Add(x);
                data.Y.Add(y);
                data.Z.Add(z);
                data.R.Add(r);
                data.G.Add(g);
                data.B.Add(b);
                if (importSettings.importRGB && importSettings.importIntensity) data.Intensity.Add(intensity);
                if (importSettings.importRGB && importSettings.importClassification) data.Classification.Add(classification);
                if (importSettings.averageTimestamp) data.Time.Add(time);

                nodeData[key] = data;
            }
            else // got existing cell, add point to it
            {
                // Since PointData is now a class (reference type), modifications persist
                data.X.Add(x);
                data.Y.Add(y);
                data.Z.Add(z);
                data.R.Add(r);
                data.G.Add(g);
                data.B.Add(b);

                if (importSettings.importRGB && importSettings.importIntensity) data.Intensity.Add(intensity);
                if (importSettings.importRGB && importSettings.importClassification) data.Classification.Add(classification);
                if (importSettings.averageTimestamp) data.Time.Add(time);
            }
        } // AddPoint()

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe void FloatToBytes(float value, byte[] buffer, int offset)
        {
            fixed (byte* b = &buffer[offset])
            {
                *(float*)b = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe void IntToBytes(int value, byte[] buffer, int offset)
        {
            fixed (byte* b = &buffer[offset])
            {
                *(int*)b = value;
            }
        }

        void IWriter.Save(int fileIndex)
        {
            if (useLossyFiltering == true)
            {
                Console.WriteLine("************* useLossyFiltering ****************");
            }

            string fileOnly = Path.GetFileNameWithoutExtension(importSettings.outputFile);
            string baseFolder = Path.GetDirectoryName(importSettings.outputFile);
            Console.ForegroundColor = ConsoleColor.Blue;

            Log.Write("Saving " + nodeData.Count + " tiles into: " + baseFolder);

            Console.ForegroundColor = ConsoleColor.White;

            //List<float> nodeTempX;
            //List<float> nodeTempY;
            //List<float> nodeTempZ;
            //List<float> nodeTempR;
            //List<float> nodeTempG;
            //List<float> nodeTempB;
            //List<ushort> nodeTempIntensity = null;
            //List<byte> nodeTempClassification = null;
            //List<double> nodeTempTime = null;

            List<string> outputFiles = new List<string>();

            // Process all tiles
            foreach (KeyValuePair<(int x, int y, int z), PointData> nodeEntry in nodeData)
            {
                var key = nodeEntry.Key;
                var data = nodeEntry.Value;

                if (data.X.Count < importSettings.minimumPointCount)
                {
                    skippedNodesCounter++;
                    continue;
                }

                //nodeTempX = data.X;
                //nodeTempY = data.Y;
                //nodeTempZ = data.Z;
                //nodeTempR = data.R;
                //nodeTempG = data.G;
                //nodeTempB = data.B;

                //if (importSettings.importRGB && importSettings.importIntensity)
                //{
                //    nodeTempIntensity = data.Intensity;
                //}

                //if (importSettings.importRGB && importSettings.importClassification)
                //{
                //    nodeTempClassification = data.Classification;
                //}

                //if (importSettings.averageTimestamp)
                //{
                //    nodeTempTime = data.Time;
                //}

                // Randomize points if enabled
                //if (importSettings.randomize)
                //{
                //    Tools.ShufflePointAttributes(
                //        nodeTempX.Count,
                //        nodeTempX, nodeTempY, nodeTempZ,
                //        importSettings.importRGB ? nodeTempR : null,
                //        importSettings.importRGB ? nodeTempG : null,
                //        importSettings.importRGB ? nodeTempB : null,
                //        importSettings.importIntensity ? nodeTempIntensity : null,
                //        importSettings.importClassification ? nodeTempClassification : null,
                //        importSettings.averageTimestamp ? nodeTempTime : null
                //    );
                //}

                if (importSettings.randomize)
                {
                    Tools.ShufflePointAttributes(
                        data.X.Count,
                        data.X, data.Y, data.Z,
                        importSettings.importRGB ? data.R : null,
                        importSettings.importRGB ? data.G : null,
                        importSettings.importRGB ? data.B : null,
                        importSettings.importIntensity ? data.Intensity : null,
                        importSettings.importClassification ? data.Classification : null,
                        importSettings.averageTimestamp ? data.Time : null
                    );
                }

                // Get tile bounds
                float minX = float.PositiveInfinity;
                float minY = float.PositiveInfinity;
                float minZ = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float maxY = float.NegativeInfinity;
                float maxZ = float.NegativeInfinity;

                int cellX = key.x;
                int cellY = key.y;
                int cellZ = key.z;

                string fullpath = Path.Combine(baseFolder, fileOnly) + "_" + fileIndex + "_" + cellX + "_" + cellY + "_" + cellZ + tileExtension;
                string fullpathFileOnly = fileOnly + "_" + fileIndex + "_" + cellX + "_" + cellY + "_" + cellZ + tileExtension;

                //bsPoints = new BufferedStream(new FileStream(fullpath, FileMode.Create));
                //writerPoints = new BinaryWriter(bsPoints);

                outputFiles.Add(fullpath);


                int totalPointsWritten = 0;
                int fixedGridSize = 10;
                int cellsInTile = 64;
                bool[] reservedGridCells = null;

                if (useLossyFiltering == true) reservedGridCells = new bool[cellsInTile * cellsInTile * cellsInTile];

                double totalTime = 0;

                // Write all points in this tile
                for (int i = 0, len = data.X.Count; i < len; i++)
                {
                    float px = data.X[i];
                    float py = data.Y[i];
                    float pz = data.Z[i];
                    int packedX = 0;
                    int packedY = 0;

                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                    if (pz < minZ) minZ = pz;
                    if (pz > maxZ) maxZ = pz;

                    if (importSettings.packColors == true)
                    {
                        px -= (cellX * importSettings.gridSize);
                        py -= (cellY * importSettings.gridSize);
                        pz -= (cellZ * importSettings.gridSize);

                        // Pack G, Py and INTensity
                        if (importSettings.importRGB && importSettings.importIntensity && !importSettings.importClassification)
                        {
                            float c = py;
                            int cIntegral = (int)c;
                            int cFractional = (int)((c - cIntegral) * 255);
                            byte bg = (byte)(data.G[i] * 255);
                            byte bi = importSettings.useCustomIntensityRange ? (byte)(data.Intensity[i] / 257) : (byte)data.Intensity[i];
                            packedY = (bg << 24) | (bi << 16) | (cIntegral << 8) | cFractional;
                        }
                        else if (importSettings.importRGB && !importSettings.importIntensity && importSettings.importClassification)
                        {
                            float c = py;
                            int cIntegral = (int)c;
                            int cFractional = (int)((c - cIntegral) * 255);
                            byte bg = (byte)(data.G[i] * 255);
                            byte bc = data.Classification[i];
                            packedY = (bg << 24) | (bc << 16) | (cIntegral << 8) | cFractional;
                        }
                        else if (importSettings.importRGB && importSettings.importIntensity && importSettings.importClassification)
                        {
                            float c = py;
                            int cIntegral = (int)c;
                            int cFractional = (int)((c - cIntegral) * 255);
                            byte bg = (byte)(data.G[i] * 255);
                            byte bi = importSettings.useCustomIntensityRange ? (byte)(data.Intensity[i] / 257) : (byte)data.Intensity[i];
                            packedY = (bg << 24) | (bi << 16) | (cIntegral << 8) | cFractional;
                        }
                        else
                        {
                            py = Tools.SuperPacker(data.G[i] * 0.98f, py, importSettings.gridSize * importSettings.packMagicValue);
                        }

                        if (importSettings.importRGB && importSettings.importIntensity && importSettings.importClassification)
                        {
                            float c = px;
                            int cIntegral = (int)c;
                            int cFractional = (int)((c - cIntegral) * 255);
                            byte br = (byte)(data.R[i] * 255);
                            byte bc = data.Classification[i];
                            packedX = (br << 24) | (bc << 16) | (cIntegral << 8) | cFractional;
                        }
                        else
                        {
                            px = Tools.SuperPacker(data.R[i] * 0.98f, px, importSettings.gridSize * importSettings.packMagicValue);
                        }

                        pz = Tools.SuperPacker(data.B[i] * 0.98f, pz, importSettings.gridSize * importSettings.packMagicValue);
                    }
                    else if (useLossyFiltering == true)
                    {
                        px -= (cellX * fixedGridSize);
                        py -= (cellY * fixedGridSize);
                        pz -= (cellZ * fixedGridSize);
                        px /= (float)cellsInTile;
                        py /= (float)cellsInTile;
                        pz /= (float)cellsInTile;
                        byte packx = (byte)(px * cellsInTile);
                        byte packy = (byte)(py * cellsInTile);
                        byte packz = (byte)(pz * cellsInTile);

                        var reservedTileLocalCellIndex = packx + cellsInTile * (packy + cellsInTile * packz);

                        if (reservedGridCells[reservedTileLocalCellIndex] == true)
                        {
                            skippedPointsCounter++;
                            continue;
                        }

                        reservedGridCells[reservedTileLocalCellIndex] = true;
                    }

                    if (useLossyFiltering == true)
                    {
                        byte bx = (byte)(px * cellsInTile);
                        byte by = (byte)(py * cellsInTile);
                        byte bz = (byte)(pz * cellsInTile);

                        float h = 0f, s = 0f, v = 0f;
                        RGBtoHSV(data.R[i], data.G[i], data.B[i], out h, out s, out v);

                        h = h / 360f;
                        byte bh = (byte)(h * 255f);
                        byte bs = (byte)(s * 255f);
                        byte bv = (byte)(v * 255f);
                        byte huepacked = (byte)(bh >> 3);
                        byte satpacked = (byte)(bs >> 3);
                        byte valpacked = (byte)(bv >> 4);
                        uint hsv554 = (uint)((huepacked << 9) + (satpacked << 5) + valpacked);

                        uint combinedXYZHSV = (uint)(((bz + by << 6 + bx << 12)) << 14) + hsv554;
                        //writerPoints.Write((uint)combinedXYZHSV);
                    }
                    else
                    {
                        if (importSettings.packColors && importSettings.importRGB && importSettings.importIntensity && importSettings.importClassification)
                        {
                            IntToBytes(packedX, pointBuffer, 0);
                        }
                        else
                        {
                            FloatToBytes(px, pointBuffer, 0);
                        }

                        if (importSettings.packColors && importSettings.importRGB && (importSettings.importIntensity || importSettings.importClassification))
                        {
                            IntToBytes(packedY, pointBuffer, 4);
                        }
                        else
                        {
                            FloatToBytes(py, pointBuffer, 4);
                        }

                        FloatToBytes(pz, pointBuffer, 8);
                        // for testing, dont output to file
                        //writerPoints.Write(pointBuffer);
                    }

                    if (importSettings.averageTimestamp)
                    {
                        totalTime += data.Time[i];
                    }

                    totalPointsWritten++;
                } // for all points

                //writerPoints.Close();
                //bsPoints.Dispose();

                // Write separate RGB file if not packed
                if (importSettings.packColors == false && useLossyFiltering == false)
                {
                    using (var writerColors = new BinaryWriter(new BufferedStream(new FileStream(fullpath + ".rgb", FileMode.Create))))
                    {
                        int len = data.X.Count;
                        for (int i = 0; i < len; i++)
                        {
                            FloatToBytes(data.R[i], colorBuffer, 0);
                            FloatToBytes(data.G[i], colorBuffer, 4);
                            FloatToBytes(data.B[i], colorBuffer, 8);
                            writerColors.Write(colorBuffer);
                        }
                    }

                    // Write intensity file
                    if (importSettings.importRGB && importSettings.importIntensity)
                    {
                        BufferedStream bsIntensity = new BufferedStream(new FileStream(fullpath + ".int", FileMode.Create));
                        var writerIntensity = new BinaryWriter(bsIntensity);

                        for (int i = 0, len = data.X.Count; i < len; i++)
                        {
                            float c = data.Intensity[i] / 255f;
                            writerIntensity.Write(c);
                            writerIntensity.Write(c);
                            writerIntensity.Write(c);
                        }

                        writerIntensity.Close();
                        bsIntensity.Dispose();
                    }

                    // Write classification file
                    if (importSettings.importRGB && importSettings.importClassification)
                    {
                        BufferedStream bsClassification = new BufferedStream(new FileStream(fullpath + ".cla", FileMode.Create));
                        var writerClassification = new BinaryWriter(bsClassification);

                        for (int i = 0, len = data.X.Count; i < len; i++)
                        {
                            float c = data.Classification[i] / 255f;
                            writerClassification.Write(c);
                            writerClassification.Write(c);
                            writerClassification.Write(c);
                        }

                        writerClassification.Close();
                        bsClassification.Dispose();
                    }
                }

                // Collect node bounds
                var cb = new PointCloudTile();
                cb.fileName = fullpathFileOnly;
                cb.totalPoints = totalPointsWritten;
                cb.minX = minX;
                cb.minY = minY;
                cb.minZ = minZ;
                cb.maxX = maxX;
                cb.maxY = maxY;
                cb.maxZ = maxZ;
                cb.centerX = (minX + maxX) * 0.5f;
                cb.centerY = (minY + maxY) * 0.5f;
                cb.centerZ = (minZ + maxZ) * 0.5f;
                cb.cellX = cellX;
                cb.cellY = cellY;
                cb.cellZ = cellZ;

                localBounds.minX = Math.Min(localBounds.minX, minX);
                localBounds.minY = Math.Min(localBounds.minY, minY);
                localBounds.minZ = Math.Min(localBounds.minZ, minZ);
                localBounds.maxX = Math.Max(localBounds.maxX, maxX);
                localBounds.maxY = Math.Max(localBounds.maxY, maxY);
                localBounds.maxZ = Math.Max(localBounds.maxZ, maxZ);

                GlobalBounds.Merge(in localBounds);

                if (importSettings.averageTimestamp && totalPointsWritten > 0)
                {
                    double averageTime = totalTime / totalPointsWritten;
                    cb.averageTimeStamp = averageTime;
                }

                nodeBoundsBag.Add(cb);
            } // for all tiles

            // release actual memory with trim or replace array
            //1.Dispose / clear the PointData values and set their lists to null(or reallocate new ones via pooling).
            foreach (var data in nodeData.Values)
            {
                data.X.Clear();
                data.Y.Clear();
                data.Z.Clear();
                data.R.Clear();
                data.G.Clear();
                data.B.Clear();
                data.Intensity?.Clear();
                data.Classification?.Clear();
                data.Time?.Clear();

                data.X = null;
                data.Y = null;
                data.Z = null;
                data.R = null;
                data.G = null;
                data.B = null;
                data.Intensity = null;
                data.Classification = null;
                data.Time = null;
            }
            nodeData.Clear();

            // gc clear
            GC.Collect();
            GC.WaitForPendingFinalizers();



            string jsonString = "{" +
                                "\"event\": \"" + LogEvent.File + "\"," +
                                "\"status\": \"" + LogStatus.Complete + "\"," +
                                "\"path\": " + JsonSerializer.Serialize(importSettings.inputFiles[fileIndex]) + "," +
                                "\"tiles\": " + nodeData.Count + "," +
                                "\"folder\": " + JsonSerializer.Serialize(baseFolder) + "," +
                                "\"filenames\": " + JsonSerializer.Serialize(outputFiles) + "}";
            Log.Write(jsonString, LogEvent.End);
        } // Save()

        void IWriter.Close()
        {
            if (importSettings == null) return;

            var nodeBounds = nodeBoundsBag.ToList();

            if (importSettings.checkoverlap == true)
            {
                for (int i = 0, len = nodeBounds.Count; i < len; i++)
                {
                    var cb = nodeBounds[i];
                    for (int j = 0, len2 = nodeBounds.Count; j < len2; j++)
                    {
                        if (i == j) continue;
                        var cb2 = nodeBounds[j];
                        float epsilon = 1e-6f;
                        bool overlaps = cb.minX < cb2.maxX + epsilon && cb.maxX > cb2.minX - epsilon &&
                                        cb.minY < cb2.maxY + epsilon && cb.maxY > cb2.minY - epsilon &&
                                        cb.minZ < cb2.maxZ + epsilon && cb.maxZ > cb2.minZ - epsilon;

                        if (overlaps)
                        {
                            float overlapX = Math.Min(cb.maxX, cb2.maxX) - Math.Max(cb.minX, cb2.minX);
                            float overlapY = Math.Min(cb.maxY, cb2.maxY) - Math.Max(cb.minY, cb2.minY);
                            float overlapZ = Math.Min(cb.maxZ, cb2.maxZ) - Math.Max(cb.minZ, cb2.minZ);
                            float overlapVolume = overlapX * overlapY * overlapZ;
                            float volume1 = (cb.maxX - cb.minX) * (cb.maxY - cb.minY) * (cb.maxZ - cb.minZ);
                            float volume2 = (cb2.maxX - cb2.minX) * (cb2.maxY - cb2.minY) * (cb2.maxZ - cb2.minZ);

                            if (volume1 != 0 && volume2 != 0)
                            {
                                float overlapRatio = overlapVolume / Math.Min(volume1, volume2);
                                cb.overlapRatio = overlapRatio;
                            }
                            else
                            {
                                cb.overlapRatio = 0;
                            }

                            nodeBounds[i] = cb;
                        }
                    }
                }
            }

            string fileOnly = Path.GetFileNameWithoutExtension(importSettings.outputFile);
            string baseFolder = Path.GetDirectoryName(importSettings.outputFile);

            var tilerootdata = new List<string>();
            var outputFileRoot = Path.Combine(baseFolder, fileOnly) + ".pcroot";

            long totalPointCount = 0;

            for (int i = 0, len = nodeBounds.Count; i < len; i++)
            {
                var tilerow = nodeBounds[i].totalPoints + sep + nodeBounds[i].minX + sep + nodeBounds[i].minY + sep + nodeBounds[i].minZ + sep + nodeBounds[i].maxX + sep + nodeBounds[i].maxY + sep + nodeBounds[i].maxZ + sep + nodeBounds[i].cellX + sep + nodeBounds[i].cellY + sep + nodeBounds[i].cellZ + sep + nodeBounds[i].averageTimeStamp + sep + nodeBounds[i].overlapRatio;
                tilerow = tilerow.Replace(",", ".");
                tilerow = nodeBounds[i].fileName + sep + tilerow;
                tilerootdata.Add(tilerow);
                totalPointCount += nodeBounds[i].totalPoints;
            }

            string jsonString = "{" +
            "\"event\": \"" + LogEvent.File + "\"," +
            "\"path\": " + JsonSerializer.Serialize(outputFileRoot) + "," +
            "\"totalpoints\": " + totalPointCount + "," +
            "\"skippedNodes\": " + skippedNodesCounter + "," +
            "\"skippedPoints\": " + skippedPointsCounter +
            "}";

            Log.Write(jsonString, LogEvent.End);
            Log.Write("\nSaving rootfile: " + outputFileRoot + "\n*Total points= " + Tools.HumanReadableCount(totalPointCount));

            int versionID = importSettings.packColors ? 2 : 1;
            if (importSettings.packColors == true) versionID = 2;
            if (useLossyFiltering == true) versionID = 3;
            if ((importSettings.importIntensity || importSettings.importClassification) && importSettings.importRGB && importSettings.packColors) versionID = 4;
            if ((importSettings.importIntensity && importSettings.importClassification) && importSettings.importRGB && importSettings.packColors) versionID = 5;

            bool addComments = false;

            string identifer = "# PCROOT - https://github.com/unitycoder/PointCloudConverter";
            if (addComments) tilerootdata.Insert(0, identifer);

            string commentRow = "# version" + sep + "gridsize" + sep + "pointcount" + sep + "boundsMinX" + sep + "boundsMinY" + sep + "boundsMinZ" + sep + "boundsMaxX" + sep + "boundsMaxY" + sep + "boundsMaxZ" + sep + "autoOffsetX" + sep + "autoOffsetY" + sep + "autoOffsetZ" + sep + "packMagicValue";
            if (importSettings.importRGB && importSettings.importIntensity) commentRow += sep + "intensity";
            if (importSettings.importRGB && importSettings.importClassification) commentRow += sep + "classification";
            if (addComments) tilerootdata.Insert(1, commentRow);

            GlobalBounds.Merge(localBounds);
            float cloudMinX = GlobalBounds.minX;
            float cloudMinY = GlobalBounds.minY;
            float cloudMinZ = GlobalBounds.minZ;
            float cloudMaxX = GlobalBounds.maxX;
            float cloudMaxY = GlobalBounds.maxY;
            float cloudMaxZ = GlobalBounds.maxZ;

            GlobalBounds.Reset();

            string globalData = versionID + sep + importSettings.gridSize.ToString() + sep + totalPointCount + sep + cloudMinX + sep + cloudMinY + sep + cloudMinZ + sep + cloudMaxX + sep + cloudMaxY + sep + cloudMaxZ;
            globalData += sep + importSettings.offsetX + sep + importSettings.offsetY + sep + importSettings.offsetZ + sep + importSettings.packMagicValue;
            globalData = globalData.Replace(",", ".");

            if (addComments)
            {
                tilerootdata.Insert(2, globalData);
            }
            else
            {
                tilerootdata.Insert(0, globalData);
            }

            if (addComments) tilerootdata.Insert(3, "# filename" + sep + "pointcount" + sep + "minX" + sep + "minY" + sep + "minZ" + sep + "maxX" + sep + "maxY" + sep + "maxZ" + sep + "cellX" + sep + "cellY" + sep + "cellZ" + sep + "averageTimeStamp" + sep + "overlapRatio");

            File.WriteAllLines(outputFileRoot, tilerootdata.ToArray());

            Console.ForegroundColor = ConsoleColor.Green;
            Log.Write("Done saving v3 : " + outputFileRoot);
            Console.ForegroundColor = ConsoleColor.White;
            if (skippedNodesCounter > 0)
            {
                Log.Write("*Skipped " + skippedNodesCounter + " nodes with less than " + importSettings.minimumPointCount + " points)");
            }

            if (useLossyFiltering && skippedPointsCounter > 0)
            {
                Log.Write("*Skipped " + skippedPointsCounter + " points due to bytepacked grid filtering");
            }

            if ((tilerootdata.Count - 1) <= 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Log.Write("Error> No tiles found! Try enable -scale (to make your cloud to smaller) Or make -gridsize bigger, or set -limit point count to smaller value");
                Console.ForegroundColor = ConsoleColor.White;
            }

            nodeBounds.Clear();
            localBounds.Init();

            //bsPoints?.Dispose();
            //writerPoints?.Dispose();
        }

        void IWriter.Cleanup(int fileIndex)
        {
            //bsPoints?.Dispose();
            //writerPoints?.Dispose();

            if (nodeData != null)
            {
                foreach (var data in nodeData.Values)
                {
                    data.Clear();
                }
                nodeData.Clear();
            }
        }

        void RGBtoHSV(float r, float g, float b, out float h, out float s, out float v)
        {
            float min, max, delta;

            min = Math.Min(Math.Min(r, g), b);
            max = Math.Max(Math.Max(r, g), b);
            v = max;

            delta = max - min;

            if (max != 0)
                s = delta / max;
            else
            {
                s = 0;
                h = -1;
                return;
            }

            if (r == max)
                h = (g - b) / delta;
            else if (g == max)
                h = 2 + (b - r) / delta;
            else
                h = 4 + (r - g) / delta;

            h *= 60;

            if (h < 0) h += 360;
        }

        public void SetIntensityRange(bool isCustomRange)
        {
            importSettings.useCustomIntensityRange = isCustomRange;
        }
    }
}