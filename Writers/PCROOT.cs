﻿// PCROOT (v3) Exporter https://github.com/unitycoder/UnityPointCloudViewer/wiki/Binary-File-Format-Structure#custom-v3-tiles-pcroot-and-pct-rgb

using PointCloudConverter.Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PointCloudConverter.Writers
{
    public class PCROOT : IWriter
    {
        const string tileExtension = ".pct";
        const string sep = "|";

        ImportSettings importSettings;
        BufferedStream bsPoints = null;
        BinaryWriter writerPoints = null;

        static List<PointCloudTile> nodeBounds = new List<PointCloudTile>(); // for all tiles

        // our nodes (=tiles, =grid cells), string is tileID and float are X,Y,Z,R,G,B values
        Dictionary<int, List<float>> nodeX = new Dictionary<int, List<float>>();
        Dictionary<int, List<float>> nodeY = new Dictionary<int, List<float>>();
        Dictionary<int, List<float>> nodeZ = new Dictionary<int, List<float>>();

        Dictionary<int, List<float>> nodeR = new Dictionary<int, List<float>>();
        Dictionary<int, List<float>> nodeG = new Dictionary<int, List<float>>();
        Dictionary<int, List<float>> nodeB = new Dictionary<int, List<float>>();

        Dictionary<int, List<float>> nodeIntensity = new Dictionary<int, List<float>>();
        Dictionary<int, List<double>> nodeTime = new Dictionary<int, List<double>>();

        static float cloudMinX = float.PositiveInfinity;
        static float cloudMinY = float.PositiveInfinity;
        static float cloudMinZ = float.PositiveInfinity;
        static float cloudMaxX = float.NegativeInfinity;
        static float cloudMaxY = float.NegativeInfinity;
        static float cloudMaxZ = float.NegativeInfinity;

        bool IWriter.InitWriter(ImportSettings _importSettings, int _pointCount)
        {
            var res = true;

            // clear old nodes
            nodeX.Clear();
            nodeY.Clear();
            nodeZ.Clear();
            nodeR.Clear();
            nodeG.Clear();
            nodeB.Clear();
            nodeIntensity.Clear();
            nodeTime.Clear();

            bsPoints = null;
            writerPoints = null;
            importSettings = _importSettings;

            return res;
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

        void IWriter.Close()
        {

        }

        void IWriter.Cleanup(int fileIndex)
        {

        }

        void IWriter.Randomize()
        {

        }

        int Hash(int x, int y, int z)
        {
            unchecked
            {
                // Apply offset to ensure all values are positive
                x += OFFSET;
                y += OFFSET;
                z += OFFSET;

                // Combine the values into a single hash using a method that can handle larger ranges
                long combined = ((long)x << 40) | ((long)y << 20) | (long)z;
                return combined.GetHashCode();
            }
        }

        const int OFFSET = 12345678;

        (int x, int y, int z) Unhash(int hash)
        {
            // Restore the original x, y, z values
            long combined = hash;

            int z = (int)(combined & ((1L << 20) - 1));
            combined >>= 20;
            int y = (int)(combined & ((1L << 20) - 1));
            combined >>= 20;
            int x = (int)combined;

            // Remove the offset to get original values
            x -= OFFSET;
            y -= OFFSET;
            z -= OFFSET;

            return (x, y, z);
        }

        void IWriter.AddPoint(int index, float x, float y, float z, float r, float g, float b, bool hasIntensity, float i, bool hasTime, double time)
        {
            // get global all clouds bounds
            //if (x < cloudMinX) cloudMinX = x;
            //if (x > cloudMaxX) cloudMaxX = x;
            //if (y < cloudMinY) cloudMinY = y;
            //if (y > cloudMaxY) cloudMaxY = y;
            //if (z < cloudMinZ) cloudMinZ = z;
            //if (z > cloudMaxZ) cloudMaxZ = z;
            cloudMinX = Math.Min(cloudMinX, x);
            cloudMaxX = Math.Max(cloudMaxX, x);
            cloudMinY = Math.Min(cloudMinY, y);
            cloudMaxY = Math.Max(cloudMaxY, y);
            cloudMinZ = Math.Min(cloudMinZ, z);
            cloudMaxZ = Math.Max(cloudMaxZ, z);

            float gridSize = importSettings.gridSize;

            // add to correct cell, MOVE to writer
            // TODO handle bytepacked gridsize here
            int cellX = (int)(x / gridSize);
            int cellY = (int)(y / gridSize);
            int cellZ = (int)(z / gridSize);

            // collect point to its cell node, TODO optimize this is ~23% of total time?
            //string key = cellX + "_" + cellY + "_" + cellZ;
            int key = Hash(cellX, cellY, cellZ);

            if (nodeX.ContainsKey(key))
            {
                nodeX[key].Add(x);
                nodeY[key].Add(y);
                nodeZ[key].Add(z);

                nodeR[key].Add(r);
                nodeG[key].Add(g);
                nodeB[key].Add(b);

                if (hasIntensity == true) nodeIntensity[key].Add(i);
                if (hasTime == true) nodeTime[key].Add(time);
            }
            else // create new list for this key
            {
                // NOTE if memory error here, use smaller gridsize (single array maxsize is ~2gb)
                nodeX[key] = new List<float> { x };
                nodeY[key] = new List<float> { y };
                nodeZ[key] = new List<float> { z };
                nodeR[key] = new List<float> { r };
                nodeG[key] = new List<float> { g };
                nodeB[key] = new List<float> { b };

                if (hasIntensity == true)
                {
                    nodeIntensity[key] = new List<float> { i };
                }

                if (hasTime == true)
                {
                    nodeTime[key] = new List<double> { time };
                }
            }
        }

        void IWriter.Save(int fileIndex)
        {
            // TEST 
            bool useLossyFiltering = false;
            if (useLossyFiltering == true)
            {
                Console.WriteLine("************* useLossyFiltering ****************");
            }

            int skippedNodesCounter = 0;
            int skippedPointsCounter = 0;

            string fileOnly = Path.GetFileNameWithoutExtension(importSettings.outputFile);
            string baseFolder = Path.GetDirectoryName(importSettings.outputFile);
            // TODO no need colors for json.. could move this inside custom logger, so that it doesnt do anything, if json
            Console.ForegroundColor = ConsoleColor.Blue;

            // TODO add enum for status

            string jsonString = "{" +
            "\"event\": \"" + LogEvent.File + "\"," +
            "\"status\": \"" + LogStatus.Complete + "\"," +
            "\"path\": " + JsonSerializer.Serialize(importSettings.inputFiles[fileIndex]) + "," +
            "\"tiles\": " + nodeX.Count + "," +
            "\"folder\": " + JsonSerializer.Serialize(baseFolder) + "}";

            // TODO combine 2 outputs.. only other one shows up now
            Log.WriteLine("Saving " + nodeX.Count + " tiles to folder: " + baseFolder);
            Log.WriteLine(jsonString, LogEvent.End);

            Console.ForegroundColor = ConsoleColor.White;

            List<float> nodeTempX;
            List<float> nodeTempY;
            List<float> nodeTempZ;

            List<float> nodeTempR;
            List<float> nodeTempG;
            List<float> nodeTempB;

            List<float> nodeTempIntensity = null;
            List<double> nodeTempTime = null;

            // process all tiles
            foreach (KeyValuePair<int, List<float>> nodeData in nodeX)
            {
                if (nodeData.Value.Count < importSettings.minimumPointCount)
                {
                    skippedNodesCounter++;
                    continue;
                }

                nodeTempX = nodeData.Value;

                //string key = nodeData.Key;
                int key = nodeData.Key;

                nodeTempY = nodeY[key];
                nodeTempZ = nodeZ[key];

                nodeTempR = nodeR[key];
                nodeTempG = nodeG[key];
                nodeTempB = nodeB[key];

                // collect both
                if (importSettings.importRGB == true && importSettings.importIntensity == true)
                {
                    nodeTempIntensity = nodeIntensity[key];
                }

                if (importSettings.averageTimestamp == true)
                {
                    nodeTempTime = nodeTime[key];
                }


                // randomize points in this node
                if (importSettings.randomize == true)
                {
                    if (importSettings.importRGB == true && importSettings.importIntensity == true)
                    {
                        if (importSettings.averageTimestamp == true)
                        {
                            Tools.Shuffle(Tools.rnd, ref nodeTempX, ref nodeTempY, ref nodeTempZ, ref nodeTempR, ref nodeTempG, ref nodeTempB, ref nodeTempIntensity, ref nodeTempTime);
                        }
                        else
                        {
                            Tools.Shuffle(Tools.rnd, ref nodeTempX, ref nodeTempY, ref nodeTempZ, ref nodeTempR, ref nodeTempG, ref nodeTempB, ref nodeTempIntensity);
                        }
                    }
                    else
                    {
                        if (importSettings.averageTimestamp == true)
                        {
                            Tools.Shuffle(Tools.rnd, ref nodeTempX, ref nodeTempY, ref nodeTempZ, ref nodeTempR, ref nodeTempG, ref nodeTempB, ref nodeTempTime);
                        }
                        else
                        {
                            Tools.Shuffle(Tools.rnd, ref nodeTempX, ref nodeTempY, ref nodeTempZ, ref nodeTempR, ref nodeTempG, ref nodeTempB);
                        }
                    }
                }

                // get this node bounds, TODO but we know node(grid cell) x,y,z values?
                float minX = float.PositiveInfinity;
                float minY = float.PositiveInfinity;
                float minZ = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float maxY = float.NegativeInfinity;
                float maxZ = float.NegativeInfinity;

                // build tilefile for points in this node
                string fullpath = Path.Combine(baseFolder, fileOnly) + "_" + fileIndex + "_" + key + tileExtension;
                string fullpathFileOnly = fileOnly + "_" + fileIndex + "_" + key + tileExtension;

                // if batch mode (more than 1 file), FIXME generates new unique filename..but why not overwrite?
                if (fileIndex > 0 && File.Exists(fullpath))
                {
                    //Console.WriteLine("File already exists! " + fullpath);
                    Int32 unixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                    fullpath = Path.Combine(baseFolder, fileOnly) + "_" + fileIndex + "_" + key + "_r" + (unixTimestamp) + tileExtension;
                    fullpathFileOnly = fileOnly + "_" + fileIndex + "_" + key + tileExtension;
                }

                // prepare file
                bsPoints = new BufferedStream(new FileStream(fullpath, FileMode.Create));
                writerPoints = new BinaryWriter(bsPoints);

                int cellX = 0;
                int cellY = 0;
                int cellZ = 0;

                // FIXME this is wrong value, if file is appended.. but for now append is disabled
                int totalPointsWritten = 0;

                // TESTING for lossy
                int fixedGridSize = 10; // one tile is this size
                int cellsInTile = 64; // how many subtiles in one tile
                                      //float center = (1f / (float)cells) / 2f;
                bool[] reservedGridCells = null;

                if (useLossyFiltering == true) reservedGridCells = new bool[cellsInTile * cellsInTile * cellsInTile];

                //Console.WriteLine("nodeTempX.Count="+ nodeTempX.Count);

                double totalTime = 0; // for average timestamp

                // loop and output all points within that node/tile
                for (int i = 0, len = nodeTempX.Count; i < len; i++)
                {
                    // skip points
                    if (importSettings.skipPoints == true && (i % importSettings.skipEveryN == 0)) continue;

                    // keep points
                    if (importSettings.keepPoints == true && (i % importSettings.keepEveryN != 0)) continue;

                    // get original world positions
                    float px = nodeTempX[i];
                    float py = nodeTempY[i];
                    float pz = nodeTempZ[i];
                    int packed = 0;
                    // FIXME bounds is wrong if appended (but append is disabled now), should include previous data also, but now append is disabled.. also probably should use known cell xyz bounds directly
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                    if (pz < minZ) minZ = pz;
                    if (pz > maxZ) maxZ = pz;

                    if (importSettings.packColors == true)
                    {
                        // get local coords within tile
                        //var keys = nodeData.Key.Split('_');
                        (int restoredX, int restoredY, int restoredZ) = Unhash(nodeData.Key);
                        cellX = restoredX;
                        cellY = restoredY;
                        cellZ = restoredZ;

                        // TODO no need to parse, we should know these values?
                        //cellX = int.Parse(keys[0]);
                        //cellY = int.Parse(keys[1]);
                        //cellZ = int.Parse(keys[2]);
                        // offset to local coords (within tile)
                        px -= (cellX * importSettings.gridSize);
                        py -= (cellY * importSettings.gridSize);
                        pz -= (cellZ * importSettings.gridSize);

                        // pack G, PY and INTensity
                        if (importSettings.importRGB == true && importSettings.importIntensity == true)
                        {
                            float c = py;
                            int cIntegral = (int)c;
                            int cFractional = (int)((c - cIntegral) * 255);
                            byte br = (byte)(nodeTempG[i] * 255);
                            byte bi = (byte)(nodeTempIntensity[i] * 255);
                            packed = (br << 24) | (bi << 16) | (cIntegral << 8) | cFractional;
                        }
                        else
                        {
                            // pack green and y
                            py = Tools.SuperPacker(nodeTempG[i] * 0.98f, py, importSettings.gridSize * importSettings.packMagicValue);
                        }

                        // pack red and x
                        px = Tools.SuperPacker(nodeTempR[i] * 0.98f, px, importSettings.gridSize * importSettings.packMagicValue);
                        // pack blue and z
                        pz = Tools.SuperPacker(nodeTempB[i] * 0.98f, pz, importSettings.gridSize * importSettings.packMagicValue);

                        // TODO pack intensity also?
                        //if (importSettings.importIntensity)
                        //{
                        //    //px = Tools.SuperPacker(nodeTempIntensity[i] * 0.98f, px, importSettings.gridSize * importSettings.packMagicValue);
                        //    //py = Tools.SuperPacker(nodeTempIntensity[i] * 0.98f, py, importSettings.gridSize * importSettings.packMagicValue);
                        //    //pz = Tools.SuperPacker(nodeTempIntensity[i] * 0.98f, pz, importSettings.gridSize * importSettings.packMagicValue);
                        //    //px = Tools.SuperPacker3(nodeTempR[i] * 0.98f, nodeTempIntensity[i] * 0.98f, px);
                        //    //py = Tools.SuperPacker3(nodeTempG[i] * 0.98f, nodeTempIntensity[i] * 0.98f, py);
                        //    //pz = Tools.SuperPacker3(nodeTempB[i] * 0.98f, nodeTempIntensity[i] * 0.98f, pz);
                        //}

                    }
                    else if (useLossyFiltering == true) // test lossy, not regular packed
                    {
                        // get local coords within tile
                        //var keys = nodeData.Key.Split('_');
                        // TODO no need to parse, we should know these values? these are world cell grid coors
                        // TODO take reserved grid cells earlier, when reading points! not here on 2nd pass..
                        //cellX = int.Parse(keys[0]);
                        //cellY = int.Parse(keys[1]);
                        //cellZ = int.Parse(keys[2]);
                        // offset point inside local tile
                        (int restoredX, int restoredY, int restoredZ) = Unhash(nodeData.Key);
                        cellX = restoredX;
                        cellY = restoredY;
                        cellZ = restoredZ;
                        px -= (cellX * fixedGridSize);
                        py -= (cellY * fixedGridSize);
                        pz -= (cellZ * fixedGridSize);
                        //byte packx = (byte)(px * cells);
                        //byte packy = (byte)(py * cells);
                        //byte packz = (byte)(pz * cells);
                        // normalize into tile coords
                        px /= (float)cellsInTile;
                        py /= (float)cellsInTile;
                        pz /= (float)cellsInTile;
                        byte packx = (byte)(px * cellsInTile);
                        byte packy = (byte)(py * cellsInTile);
                        byte packz = (byte)(pz * cellsInTile);

                        var reservedTileLocalCellIndex = packx + cellsInTile * (packy + cellsInTile * packz);

                        //if (i < 10) Log.WriteLine("cellX:" + cellX + " cellY:" + cellY + " cellZ:" + cellZ + "  px: " + px + " py: " + py + " pz: " + pz + " localIndex: " + reservedTileLocalCellIndex + " packx: " + packx + " packy: " + packy + " packz: " + packz);

                        // TODO could decide which point is more important or stronger color?
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

                        float h = 0f;
                        float s = 0f;
                        float v = 0f;
                        RGBtoHSV(nodeTempR[i], nodeTempG[i], nodeTempB[i], out h, out s, out v);

                        //if (i < 3) Console.WriteLine("h: " + h + " s: " + s + " v: " + v);

                        // fix values
                        h = h / 360f;

                        byte bh = (byte)(h * 255f);
                        byte bs = (byte)(s * 255f);
                        byte bv = (byte)(v * 255f);
                        // cut off 3 bits (from 8 bits)
                        byte huepacked = (byte)(bh >> 3);
                        // cut off 3 bits, then move in the middle bits
                        byte satpacked = (byte)(bs >> 3);
                        // cut off 4 bits (from 8 bits)
                        byte valpacked = (byte)(bv >> 4);
                        // combine H (5 bits), S (5 bits), V (4 bits)
                        uint hsv554 = (uint)((huepacked << 9) + (satpacked << 5) + valpacked);

                        uint combinedXYZHSV = (uint)(((bz + by << 6 + bx << 12)) << 14) + hsv554;
                        writerPoints.Write((uint)combinedXYZHSV);
                    }
                    else // write packed and unpacked
                    {
                        writerPoints.Write(px);
                        if (importSettings.packColors == true && importSettings.importRGB == true && importSettings.importIntensity == true)
                        {
                            writerPoints.Write(packed);
                        }
                        else
                        {
                            writerPoints.Write(py);
                        }
                        writerPoints.Write(pz);
                    }

                    if (importSettings.averageTimestamp == true)
                    {
                        double ptime = nodeTempTime[i]; // time for this single point
                        totalTime += ptime;
                        //Console.WriteLine(ptime);
                    }

                    totalPointsWritten++;
                } // loop all points in tile (node)


                // close tile file
                writerPoints.Close();
                bsPoints.Dispose();

                // not packed
                if (importSettings.packColors == false && useLossyFiltering == false)
                {
                    // save separate RGB
                    BufferedStream bsColors;
                    bsColors = new BufferedStream(new FileStream(fullpath + ".rgb", FileMode.Create));
                    var writerColors = new BinaryWriter(bsColors);

                    // output all points within that node cell
                    for (int i = 0, len = nodeTempX.Count; i < len; i++)
                    {
                        // skip points
                        if (importSettings.skipPoints == true && (i % importSettings.skipEveryN == 0)) continue;

                        // keep points
                        if (importSettings.keepPoints == true && (i % importSettings.keepEveryN != 0)) continue;

                        //if (i < 1000) Console.WriteLine(nodeTempR[i] + ", " + nodeTempG[i] + ", " + nodeTempB[i]);

                        writerColors.Write(nodeTempR[i]);
                        writerColors.Write(nodeTempG[i]);
                        writerColors.Write(nodeTempB[i]);
                    } // loop all point in cell cells

                    // close tile/node
                    writerColors.Close();
                    bsColors.Dispose();

                    // TESTING save separate Intensity, if both rgb and intensity are enabled
                    if (importSettings.importRGB == true && importSettings.importIntensity == true)
                    {
                        BufferedStream bsIntensity;
                        bsIntensity = new BufferedStream(new FileStream(fullpath + ".int", FileMode.Create));
                        var writerIntensity = new BinaryWriter(bsIntensity);

                        // output all points within that node cell
                        for (int i = 0, len = nodeTempX.Count; i < len; i++)
                        {
                            // skip points
                            if (importSettings.skipPoints == true && (i % importSettings.skipEveryN == 0)) continue;

                            // keep points
                            if (importSettings.keepPoints == true && (i % importSettings.keepEveryN != 0)) continue;

                            // TODO write as byte (not RGB floats)
                            writerIntensity.Write(nodeTempIntensity[i]);
                            writerIntensity.Write(nodeTempIntensity[i]);
                            writerIntensity.Write(nodeTempIntensity[i]);
                        } // loop all point in cell cells

                        // close tile/node
                        writerIntensity.Close();
                        bsIntensity.Dispose();
                    }

                } // if packColors == false && useLossyFiltering == false

                // collect node bounds, name and pointcount
                var cb = new PointCloudTile();
                cb.fileName = fullpathFileOnly;
                //cb.totalPoints = nodeTempX.Count;
                cb.totalPoints = totalPointsWritten;

                // get bounds and cell XYZ
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

                if (importSettings.averageTimestamp == true && totalPointsWritten > 0)
                {
                    double averageTime = totalTime / totalPointsWritten;
                    //Console.WriteLine("averageTime: " + averageTime);
                    cb.averageTimeStamp = averageTime;
                }

                nodeBounds.Add(cb);
            } // loop all nodes/tiles foreach

            // save rootfile
            // only save after last file, TODO should save this if process fails or user cancels, so no need to start from 0 again.. but then needs some merge or continue from index n feature
            if (fileIndex == (importSettings.maxFiles - 1))
            {
                // check if any tile overlaps with other tiles
                if (importSettings.checkoverlap == true)
                {
                    for (int i = 0, len = nodeBounds.Count; i < len; i++)
                    {
                        var cb = nodeBounds[i];
                        // check if this tile overlaps with other tiles
                        for (int j = 0, len2 = nodeBounds.Count; j < len2; j++)
                        {
                            if (i == j) continue; // skip self
                            var cb2 = nodeBounds[j];
                            // check if this tile overlaps with other tile
                            float epsilon = 1e-6f;
                            bool overlaps = cb.minX < cb2.maxX + epsilon && cb.maxX > cb2.minX - epsilon &&
                                            cb.minY < cb2.maxY + epsilon && cb.maxY > cb2.minY - epsilon &&
                                            cb.minZ < cb2.maxZ + epsilon && cb.maxZ > cb2.minZ - epsilon;

                            if (overlaps)
                            {
                                // calculate overlap ratio
                                float overlapX = Math.Min(cb.maxX, cb2.maxX) - Math.Max(cb.minX, cb2.minX);
                                float overlapY = Math.Min(cb.maxY, cb2.maxY) - Math.Max(cb.minY, cb2.minY);
                                float overlapZ = Math.Min(cb.maxZ, cb2.maxZ) - Math.Max(cb.minZ, cb2.minZ);
                                float overlapVolume = overlapX * overlapY * overlapZ;
                                float volume1 = (cb.maxX - cb.minX) * (cb.maxY - cb.minY) * (cb.maxZ - cb.minZ);
                                float volume2 = (cb2.maxX - cb2.minX) * (cb2.maxY - cb2.minY) * (cb2.maxZ - cb2.minZ);

                                // check if the volume of either tile is zero
                                if (volume1 != 0 && volume2 != 0)
                                {
                                    float overlapRatio = overlapVolume / Math.Min(volume1, volume2);
                                    cb.overlapRatio = overlapRatio;
                                }
                                else
                                {
                                    cb.overlapRatio = 0; // or any other appropriate value
                                }

                                nodeBounds[i] = cb;
                            }
                        }
                    }
                }

                var tilerootdata = new List<string>();
                var outputFileRoot = Path.Combine(baseFolder, fileOnly) + ".pcroot";

                // add to tileroot list
                long totalPointCount = 0;
                for (int i = 0, len = nodeBounds.Count; i < len; i++)
                {
                    var tilerow = nodeBounds[i].fileName + sep + nodeBounds[i].totalPoints + sep + nodeBounds[i].minX + sep + nodeBounds[i].minY + sep + nodeBounds[i].minZ + sep + nodeBounds[i].maxX + sep + nodeBounds[i].maxY + sep + nodeBounds[i].maxZ + sep + nodeBounds[i].cellX + sep + nodeBounds[i].cellY + sep + nodeBounds[i].cellZ + sep + nodeBounds[i].averageTimeStamp + sep + nodeBounds[i].overlapRatio;
                    tilerootdata.Add(tilerow);
                    totalPointCount += nodeBounds[i].totalPoints;
                }

                jsonString = "{" +
                "\"event\": \"" + LogEvent.File + "\"," +
                "\"path\": " + JsonSerializer.Serialize(outputFileRoot) + "," +
                "\"totalpoints\": " + totalPointCount + "," +
                "\"skippedNodes\": " + skippedNodesCounter + "," +
                "\"skippedPoints\": " + skippedPointsCounter + "" +
                "}";

                Log.WriteLine(jsonString, LogEvent.End);
                Log.WriteLine("\nSaving rootfile: " + outputFileRoot + "\n*Total points= " + Tools.HumanReadableCount(totalPointCount));

                int versionID = importSettings.packColors ? 2 : 1; // (1 = original, 2 = packed v3 format)
                if (importSettings.packColors == true) versionID = 2;
                if (useLossyFiltering == true) versionID = 3;
                if (importSettings.importIntensity == true && importSettings.importRGB && importSettings.packColors) versionID = 4; // new int packed format

                // add comment to first row (version, gridsize, pointcount, boundsMinX, boundsMinY, boundsMinZ, boundsMaxX, boundsMaxY, boundsMaxZ)
                string identifer = "# PCROOT - https://github.com/unitycoder/PointCloudConverter";
                tilerootdata.Insert(0, identifer);

                string commentRow = "# version" + sep + "gridsize" + sep + "pointcount" + sep + "boundsMinX" + sep + "boundsMinY" + sep + "boundsMinZ" + sep + "boundsMaxX" + sep + "boundsMaxY" + sep + "boundsMaxZ" + sep + "autoOffsetX" + sep + "autoOffsetY" + sep + "autoOffsetZ" + sep + "packMagicValue";
                if (importSettings.importRGB == true && importSettings.importIntensity == true) commentRow += sep + "intensity";
                tilerootdata.Insert(1, commentRow);

                // add global header settings to first row
                //               version,          gridsize,                   pointcount,             boundsMinX,       boundsMinY,       boundsMinZ,       boundsMaxX,       boundsMaxY,       boundsMaxZ
                string globalData = versionID + sep + importSettings.gridSize.ToString() + sep + totalPointCount + sep + cloudMinX + sep + cloudMinY + sep + cloudMinZ + sep + cloudMaxX + sep + cloudMaxY + sep + cloudMaxZ;
                //                  autoOffsetX,             globalOffsetY,           globalOffsetZ,           packMagic 
                globalData += sep + importSettings.offsetX + sep + importSettings.offsetY + sep + importSettings.offsetZ + sep + importSettings.packMagicValue;
                tilerootdata.Insert(2, globalData);

                // append comment for rows also
                tilerootdata.Insert(3, "# filename" + sep + "pointcount" + sep + "minX" + sep + "minY" + sep + "minZ" + sep + "maxX" + sep + "maxY" + sep + "maxZ" + sep + "cellX" + sep + "cellY" + sep + "cellZ" + sep + "averageTimeStamp" + sep + "overlapRatio");

                File.WriteAllLines(outputFileRoot, tilerootdata.ToArray());

                Console.ForegroundColor = ConsoleColor.Green;
                Log.WriteLine("Done saving v3 : " + outputFileRoot);
                Console.ForegroundColor = ConsoleColor.White;
                if (skippedNodesCounter > 0)
                {
                    Log.WriteLine("*Skipped " + skippedNodesCounter + " nodes with less than " + importSettings.minimumPointCount + " points)");
                }

                if (useLossyFiltering == true && skippedPointsCounter > 0)
                {
                    Log.WriteLine("*Skipped " + skippedPointsCounter + " points due to bytepacked grid filtering");
                }

                if ((tilerootdata.Count - 1) <= 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    // TODO add json error log
                    Log.WriteLine("Error> No tiles found! Try enable -scale (to make your cloud to smaller) Or make -gridsize bigger, or set -limit point count to smaller value");
                    Console.ForegroundColor = ConsoleColor.White;
                }

                // cleanup after last file
                nodeBounds.Clear();

                cloudMinX = float.PositiveInfinity;
                cloudMinY = float.PositiveInfinity;
                cloudMinZ = float.PositiveInfinity;
                cloudMaxX = float.NegativeInfinity;
                cloudMaxY = float.NegativeInfinity;
                cloudMaxZ = float.NegativeInfinity;
            } // if last file
        } // Save()

        void RGBtoHSV(float r, float g, float b, out float h, out float s, out float v)
        {
            float min, max, delta;

            min = Math.Min(Math.Min(r, g), b);
            max = Math.Max(Math.Max(r, g), b);
            v = max; // v

            delta = max - min;

            if (max != 0)
                s = delta / max; // s
            else
            {
                // r = g = b = 0 // s = 0, v is undefined
                s = 0;
                h = -1;
                return;
            }

            if (r == max)
                h = (g - b) / delta; // between yellow & magenta
            else if (g == max)
                h = 2 + (b - r) / delta; // between cyan & yellow
            else
                h = 4 + (r - g) / delta; // between magenta & cyan

            h *= 60; // degrees

            if (h < 0) h += 360;
        }



    } // class
} // namespace
