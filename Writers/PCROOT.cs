// PCROOT (v3) Exporter https://github.com/unitycoder/UnityPointCloudViewer/wiki/Binary-File-Format-Structure#custom-v3-tiles-pcroot-and-pct-rgb
// Low-RAM bucketed implementation:
// - AddPoint() spills each point into one of N bucket files (sequential writes).
// - Save() reads buckets back one by one, accumulates tiles up to a memory budget, flushes to final pct/rgb/int/cla.
// - Preserves: packColors, importIntensity, importClassification, averageTimestamp, minimumPointCount, randomize (chunk shuffle).
// Notes:
// - useLossyFiltering is not supported in this bucketed path (kept false).
// - randomize is done per flushed chunk (not a perfect whole-tile Fisher-Yates unless a tile fits in memory).

using PointCloudConverter.Logger;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PointCloudConverter.Writers
{
    public class PCROOT : IWriter, IDisposable
    {
        const string tileExtension = ".pct";
        const string sep = "|";

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

        static int skippedNodesCounter = 0;
        static int skippedPointsCounter = 0;

        static bool useLossyFiltering = false;

        private byte[] pointBuffer = new byte[12];
        private byte[] colorBuffer = new byte[12];

        static ILogger Log;

        // Bucket spill settings
        public int BucketCount { get; set; } = 128; // must be power of two for mask bucket selection
        public int BucketBufferBytes { get; set; } = 1 << 20; // 1 MB per bucket stream buffer

        // Memory budget for in-bucket tile accumulation during Save().
        // Set per thread/instance. Example: 4 GiB.
        public double ThreadMemoryBudgetGB { get; set; } = 4;
        public long ThreadMemoryBudgetBytes => (long)(ThreadMemoryBudgetGB * 1024 * 1024 * 1024);

        // Temp bucket infra
        private bool bucketsOpen;
        private string tempBucketFolder;
        private string[] bucketPaths;
        private FileStream[] bucketFileStreams;
        private BufferedStream[] bucketBufferedStreams;

        // Stats per tile (for pcroot + minimumPointCount skip + average timestamp)
        private readonly Dictionary<(int x, int y, int z), TileStats> tileStats = new();

        // Output folder info cached (one instance handles one file)
        private string baseFolderCached;
        private string fileOnlyCached;

        // Fixed on-disk bucket record: 48 bytes, little-endian
        // int cellX, cellY, cellZ (12)
        // float x,y,z (12) => 24
        // float r,g,b (12) => 36
        // ushort intensity (2) => 38
        // byte classification (1) => 39
        // byte flags (1) => 40
        // double time (8) => 48
        private const int BucketRecordBytes = 48;

        // flags
        private const byte FlagRGB = 1;
        private const byte FlagIntensity = 2;
        private const byte FlagClassification = 4;
        private const byte FlagTime = 8;

        struct TileStats
        {
            public long TotalPoints;
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
            public double TimeSum;
            public bool HasAny;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddPoint(float x, float y, float z, double time, bool addTime)
            {
                if (!HasAny)
                {
                    MinX = MaxX = x;
                    MinY = MaxY = y;
                    MinZ = MaxZ = z;
                    HasAny = true;
                }
                else
                {
                    if (x < MinX) MinX = x;
                    if (x > MaxX) MaxX = x;
                    if (y < MinY) MinY = y;
                    if (y > MaxY) MaxY = y;
                    if (z < MinZ) MinZ = z;
                    if (z > MaxZ) MaxZ = z;
                }

                TotalPoints++;
                if (addTime) TimeSum += time;
            }
        }

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
                CloseBucketWriters();
                DeleteBucketFolderSafe();
            }
        }

        public bool InitWriter(dynamic _importSettings, long pointCount, ILogger logger)
        {
            Log = logger;

            importSettings = (ImportSettings)(object)_importSettings;

            localBounds = new BoundsAcc();
            localBounds.Init();

            tileStats.Clear();

            bucketsOpen = false;
            tempBucketFolder = null;
            bucketPaths = null;
            bucketFileStreams = null;
            bucketBufferedStreams = null;

            baseFolderCached = null;
            fileOnlyCached = null;

            if ((BucketCount & (BucketCount - 1)) != 0)
                throw new InvalidOperationException("BucketCount must be a power of two");

            return true;
        }

        void IWriter.CreateHeader(int pointCount) { }
        void IWriter.WriteXYZ(float x, float y, float z) { }
        void IWriter.WriteRGB(float r, float g, float b) { }
        void IWriter.Randomize() { }

        void IWriter.AddPoint(int index, float x, float y, float z, float r, float g, float b, ushort intensity, double time, byte classification)
        {
            if (useLossyFiltering)
            {
                // Not supported in bucketed mode. Keep it false.
            }

            EnsureBucketWritersOpen();

            float gridSize = importSettings.gridSize;

            int cellX = (int)(x / gridSize);
            int cellY = (int)(y / gridSize);
            int cellZ = (int)(z / gridSize);

            int bucketId = BucketIdForKey(cellX, cellY, cellZ);

            byte flags = 0;
            if (importSettings.importRGB) flags |= FlagRGB;
            if (importSettings.importRGB && importSettings.importIntensity) flags |= FlagIntensity;
            if (importSettings.importRGB && importSettings.importClassification) flags |= FlagClassification;
            if (importSettings.averageTimestamp) flags |= FlagTime;

            Span<byte> rec = stackalloc byte[BucketRecordBytes];

            BinaryPrimitives.WriteInt32LittleEndian(rec.Slice(0, 4), cellX);
            BinaryPrimitives.WriteInt32LittleEndian(rec.Slice(4, 4), cellY);
            BinaryPrimitives.WriteInt32LittleEndian(rec.Slice(8, 4), cellZ);

            BinaryPrimitives.WriteSingleLittleEndian(rec.Slice(12, 4), x);
            BinaryPrimitives.WriteSingleLittleEndian(rec.Slice(16, 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(rec.Slice(20, 4), z);

            BinaryPrimitives.WriteSingleLittleEndian(rec.Slice(24, 4), r);
            BinaryPrimitives.WriteSingleLittleEndian(rec.Slice(28, 4), g);
            BinaryPrimitives.WriteSingleLittleEndian(rec.Slice(32, 4), b);

            BinaryPrimitives.WriteUInt16LittleEndian(rec.Slice(36, 2), intensity);
            rec[38] = classification;
            rec[39] = flags;

            BinaryPrimitives.WriteDoubleLittleEndian(rec.Slice(40, 8), time);

            bucketBufferedStreams[bucketId].Write(rec);
        }

        void IWriter.Save(int fileIndex)
        {
            EnsureBucketWritersOpen();
            CloseBucketWriters(); // finalize bucket files for reading

            string fileOnly = fileOnlyCached;
            string baseFolder = baseFolderCached;

            Log.Write("Bucketed Save(): buckets=" + BucketCount + ", budget=" + ThreadMemoryBudgetBytes + " bytes");

            var startedTiles = new HashSet<(int x, int y, int z)>();

            // Read each bucket, build tiles in memory up to budget, flush to disk
            byte[] ioBuf = ArrayPool<byte>.Shared.Rent(Math.Max(BucketBufferBytes, 1 << 20));

            try
            {
                var tileBuffers = new Dictionary<(int x, int y, int z), TileBuffer>(capacity: 16384);

                for (int b = 0; b < BucketCount; b++)
                {
                    string bucketPath = bucketPaths[b];
                    if (!File.Exists(bucketPath))
                        continue;

                    using var fs = new FileStream(bucketPath, FileMode.Open, FileAccess.Read, FileShare.Read, BucketBufferBytes, FileOptions.SequentialScan);
                    using var bs = new BufferedStream(fs, BucketBufferBytes);

                    tileBuffers.Clear();
                    long approxBytes = 0;

                    int carry = 0;
                    int read;

                    while ((read = bs.Read(ioBuf, carry, ioBuf.Length - carry)) > 0)
                    {
                        int total = carry + read;
                        int offset = 0;

                        while (offset + BucketRecordBytes <= total)
                        {
                            var rec = new ReadOnlySpan<byte>(ioBuf, offset, BucketRecordBytes);

                            int cellX = BinaryPrimitives.ReadInt32LittleEndian(rec.Slice(0, 4));
                            int cellY = BinaryPrimitives.ReadInt32LittleEndian(rec.Slice(4, 4));
                            int cellZ = BinaryPrimitives.ReadInt32LittleEndian(rec.Slice(8, 4));

                            float x = BinaryPrimitives.ReadSingleLittleEndian(rec.Slice(12, 4));
                            float y = BinaryPrimitives.ReadSingleLittleEndian(rec.Slice(16, 4));
                            float z = BinaryPrimitives.ReadSingleLittleEndian(rec.Slice(20, 4));

                            float r = BinaryPrimitives.ReadSingleLittleEndian(rec.Slice(24, 4));
                            float g = BinaryPrimitives.ReadSingleLittleEndian(rec.Slice(28, 4));
                            float bb = BinaryPrimitives.ReadSingleLittleEndian(rec.Slice(32, 4));

                            ushort intensity = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(36, 2));
                            byte classification = rec[38];
                            byte flags = rec[39];
                            double time = BinaryPrimitives.ReadDoubleLittleEndian(rec.Slice(40, 8));

                            var key = (cellX, cellY, cellZ);

                            if (!tileBuffers.TryGetValue(key, out var buf))
                            {
                                buf = new TileBuffer(importSettings);
                                tileBuffers.Add(key, buf);
                            }

                            buf.Add(x, y, z, r, g, bb, intensity, time, classification, flags);

                            approxBytes += buf.LastAddBytes;

                            // stats (full tile across all flushes)
                            if (!tileStats.TryGetValue(key, out var st))
                                st = default;

                            st.AddPoint(x, y, z, time, (flags & FlagTime) != 0);
                            tileStats[key] = st;

                            // Flush on budget
                            if (approxBytes >= ThreadMemoryBudgetBytes)
                            {
                                FlushTileBuffers(tileBuffers, baseFolder, fileOnly, fileIndex, startedTiles);
                                tileBuffers.Clear();
                                approxBytes = 0;
                            }

                            offset += BucketRecordBytes;
                        }

                        carry = total - offset;
                        if (carry > 0)
                            Buffer.BlockCopy(ioBuf, offset, ioBuf, 0, carry);
                    }

                    if (tileBuffers.Count > 0)
                    {
                        FlushTileBuffers(tileBuffers, baseFolder, fileOnly, fileIndex, startedTiles);
                        tileBuffers.Clear();
                    }
                }

                // Emit node bounds from tileStats (applies minimumPointCount)
                int tilesEmitted = 0;

                foreach (var kv in tileStats)
                {
                    var key = kv.Key;
                    var st = kv.Value;

                    if (st.TotalPoints < importSettings.minimumPointCount)
                    {
                        skippedNodesCounter++;
                        continue;
                    }

                    int cellX = key.x;
                    int cellY = key.y;
                    int cellZ = key.z;

                    string fullpathFileOnly = fileOnly + "_" + fileIndex + "_" + cellX + "_" + cellY + "_" + cellZ + tileExtension;

                    var cb = new PointCloudTile();
                    cb.fileName = fullpathFileOnly;
                    cb.totalPoints = (int)Math.Min(int.MaxValue, st.TotalPoints);
                    cb.minX = st.MinX; cb.minY = st.MinY; cb.minZ = st.MinZ;
                    cb.maxX = st.MaxX; cb.maxY = st.MaxY; cb.maxZ = st.MaxZ;
                    cb.centerX = (st.MinX + st.MaxX) * 0.5f;
                    cb.centerY = (st.MinY + st.MaxY) * 0.5f;
                    cb.centerZ = (st.MinZ + st.MaxZ) * 0.5f;
                    cb.cellX = cellX; cb.cellY = cellY; cb.cellZ = cellZ;

                    if (importSettings.averageTimestamp && st.TotalPoints > 0)
                        cb.averageTimeStamp = st.TimeSum / st.TotalPoints;

                    nodeBoundsBag.Add(cb);
                    tilesEmitted++;

                    localBounds.minX = Math.Min(localBounds.minX, st.MinX);
                    localBounds.minY = Math.Min(localBounds.minY, st.MinY);
                    localBounds.minZ = Math.Min(localBounds.minZ, st.MinZ);
                    localBounds.maxX = Math.Max(localBounds.maxX, st.MaxX);
                    localBounds.maxY = Math.Max(localBounds.maxY, st.MaxY);
                    localBounds.maxZ = Math.Max(localBounds.maxZ, st.MaxZ);
                }

                GlobalBounds.Merge(in localBounds);

                string jsonString = "{" +
                                    "\"event\": \"" + LogEvent.File + "\"," +
                                    "\"status\": \"" + LogStatus.Complete + "\"," +
                                    "\"path\": " + JsonSerializer.Serialize(importSettings.inputFiles[fileIndex]) + "," +
                                    "\"tiles\": " + tilesEmitted + "," +
                                    "\"folder\": " + JsonSerializer.Serialize(baseFolder) +
                                    "}";
                Log.Write(jsonString, LogEvent.End);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(ioBuf);
                DeleteBucketFolderSafe();
            }
        }

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
                var tilerow = nodeBounds[i].totalPoints + sep + nodeBounds[i].minX + sep + nodeBounds[i].minY + sep + nodeBounds[i].minZ + sep +
                             nodeBounds[i].maxX + sep + nodeBounds[i].maxY + sep + nodeBounds[i].maxZ + sep +
                             nodeBounds[i].cellX + sep + nodeBounds[i].cellY + sep + nodeBounds[i].cellZ + sep +
                             nodeBounds[i].averageTimeStamp + sep + nodeBounds[i].overlapRatio;

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

            string commentRow = "# version" + sep + "gridsize" + sep + "pointcount" + sep + "boundsMinX" + sep + "boundsMinY" + sep +
                                "boundsMinZ" + sep + "boundsMaxX" + sep + "boundsMaxY" + sep + "boundsMaxZ" + sep +
                                "autoOffsetX" + sep + "autoOffsetY" + sep + "autoOffsetZ" + sep + "packMagicValue";
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

            string globalData = versionID + sep + importSettings.gridSize.ToString() + sep + totalPointCount + sep +
                                cloudMinX + sep + cloudMinY + sep + cloudMinZ + sep +
                                cloudMaxX + sep + cloudMaxY + sep + cloudMaxZ;

            globalData += sep + importSettings.offsetX + sep + importSettings.offsetY + sep + importSettings.offsetZ + sep + importSettings.packMagicValue;
            globalData = globalData.Replace(",", ".");

            if (addComments) tilerootdata.Insert(2, globalData);
            else tilerootdata.Insert(0, globalData);

            if (addComments)
                tilerootdata.Insert(3, "# filename" + sep + "pointcount" + sep + "minX" + sep + "minY" + sep + "minZ" + sep +
                                        "maxX" + sep + "maxY" + sep + "maxZ" + sep + "cellX" + sep + "cellY" + sep + "cellZ" + sep +
                                        "averageTimeStamp" + sep + "overlapRatio");

            File.WriteAllLines(outputFileRoot, tilerootdata.ToArray());

            Log.Write("Done saving v3 : " + outputFileRoot);

            if (skippedNodesCounter > 0)
                Log.Write("*Skipped " + skippedNodesCounter + " nodes with less than " + importSettings.minimumPointCount + " points)");

            if (useLossyFiltering && skippedPointsCounter > 0)
                Log.Write("*Skipped " + skippedPointsCounter + " points due to bytepacked grid filtering");

            if ((tilerootdata.Count - 1) <= 0)
                Log.Write("Error> No tiles found! Try enable -scale or make -gridsize bigger, or set -limit point count smaller");

            nodeBounds.Clear();
            localBounds.Init();

            nodeBoundsBag.Clear();
        }

        void IWriter.Cleanup(int fileIndex)
        {
            CloseBucketWriters();
            DeleteBucketFolderSafe();
            tileStats.Clear();
        }

        public void SetIntensityRange(bool isCustomRange)
        {
            importSettings.useCustomIntensityRange = isCustomRange;
        }

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

        private void EnsureBucketWritersOpen()
        {
            if (bucketsOpen) return;

            baseFolderCached = Path.GetDirectoryName(importSettings.outputFile);
            fileOnlyCached = Path.GetFileNameWithoutExtension(importSettings.outputFile);

            string sessionId = Guid.NewGuid().ToString("N");
            tempBucketFolder = Path.Combine(baseFolderCached, fileOnlyCached + "_tmp_" + sessionId + "_buckets");
            Directory.CreateDirectory(tempBucketFolder);

            bucketPaths = new string[BucketCount];
            bucketFileStreams = new FileStream[BucketCount];
            bucketBufferedStreams = new BufferedStream[BucketCount];

            for (int i = 0; i < BucketCount; i++)
            {
                string p = Path.Combine(tempBucketFolder, "bucket_" + i + ".bin");
                bucketPaths[i] = p;

                var fs = new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.Read, BucketBufferBytes, FileOptions.SequentialScan);
                bucketFileStreams[i] = fs;
                bucketBufferedStreams[i] = new BufferedStream(fs, BucketBufferBytes);
            }

            bucketsOpen = true;
        }

        private void CloseBucketWriters()
        {
            if (!bucketsOpen) return;

            if (bucketBufferedStreams != null)
            {
                for (int i = 0; i < bucketBufferedStreams.Length; i++)
                {
                    if (bucketBufferedStreams[i] != null)
                    {
                        bucketBufferedStreams[i].Flush();
                        bucketBufferedStreams[i].Dispose();
                        bucketBufferedStreams[i] = null;
                    }
                }
            }

            if (bucketFileStreams != null)
            {
                for (int i = 0; i < bucketFileStreams.Length; i++)
                {
                    if (bucketFileStreams[i] != null)
                    {
                        bucketFileStreams[i].Dispose();
                        bucketFileStreams[i] = null;
                    }
                }
            }
        }

        private void DeleteBucketFolderSafe()
        {
            try
            {
                if (!string.IsNullOrEmpty(tempBucketFolder) && Directory.Exists(tempBucketFolder))
                    Directory.Delete(tempBucketFolder, true);
            }
            catch
            {
            }

            bucketsOpen = false;
            tempBucketFolder = null;
            bucketPaths = null;
            bucketFileStreams = null;
            bucketBufferedStreams = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashKey(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 73856093;
                h ^= y * 19349663;
                h ^= z * 83492791;
                return h;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int BucketIdForKey(int x, int y, int z)
        {
            int h = HashKey(x, y, z);
            return h & (BucketCount - 1);
        }



        private void FlushTileBuffers(
            Dictionary<(int x, int y, int z), TileBuffer> buffers,
            string baseFolder, string fileOnly, int fileIndex,
            HashSet<(int x, int y, int z)> startedTiles)
        {
            foreach (var kv in buffers)
            {
                var key = kv.Key;
                var buf = kv.Value;

                if (buf.Count == 0) { buf.Dispose(); continue; }

                int cellX = key.x, cellY = key.y, cellZ = key.z;

                string fullpath = Path.Combine(baseFolder, fileOnly) + "_" + fileIndex + "_" + cellX + "_" + cellY + "_" + cellZ + tileExtension;

                bool firstWrite = startedTiles.Add(key);
                buf.WriteToFiles(fullpath, cellX, cellY, cellZ, key, this, firstWrite);

                buf.Dispose();
            }
        }


        private sealed class TileBuffer
        {
            private readonly ImportSettings s;

            private float[] x, y, z, r, g, b;
            private ushort[] intensity;
            private byte[] classification;
            private double[] time;

            private int count;

            public long LastAddBytes { get; private set; }
            public int Count => count;

            public TileBuffer(ImportSettings settings)
            {
                s = settings;
                EnsureCapacity(256);
            }

            public void Add(float px, float py, float pz, float pr, float pg, float pb, ushort i, double t, byte c, byte flags)
            {
                EnsureCapacity(count + 1);

                x[count] = px;
                y[count] = py;
                z[count] = pz;

                r[count] = pr;
                g[count] = pg;
                b[count] = pb;

                if ((flags & FlagIntensity) != 0) intensity[count] = i;
                if ((flags & FlagClassification) != 0) classification[count] = c;
                if ((flags & FlagTime) != 0) time[count] = t;

                count++;

                long add = 24;
                if ((flags & FlagIntensity) != 0) add += 2;
                if ((flags & FlagClassification) != 0) add += 1;
                if ((flags & FlagTime) != 0) add += 8;
                LastAddBytes = add;
            }

            private static float NormalizeIntensity(ushort value, ImportSettings settings)
            {
                float max = settings.useCustomIntensityRange ? 65535f : 255f;
                float clamped = Math.Clamp(value, 0f, max);
                return clamped / max;
            }

            private void EnsureCapacity(int want)
            {
                int cap = x != null ? x.Length : 0;
                if (cap >= want) return;

                int newCap = cap == 0 ? 256 : cap;
                while (newCap < want)
                    newCap = newCap < 1024 ? newCap * 2 : newCap + (newCap / 2);

                x = Grow(x, newCap);
                y = Grow(y, newCap);
                z = Grow(z, newCap);

                r = Grow(r, newCap);
                g = Grow(g, newCap);
                b = Grow(b, newCap);

                if (s.importRGB && s.importIntensity)
                    intensity = Grow(intensity, newCap);
                if (s.importRGB && s.importClassification)
                    classification = Grow(classification, newCap);
                if (s.averageTimestamp)
                    time = Grow(time, newCap);
            }

            private static T[] Grow<T>(T[] arr, int newCap)
            {
                var pool = ArrayPool<T>.Shared;
                T[] next = pool.Rent(newCap);

                if (arr != null)
                {
                    Array.Copy(arr, 0, next, 0, arr.Length);
                    pool.Return(arr, clearArray: true);
                }

                return next;
            }

            public void Dispose()
            {
                Return(ref x);
                Return(ref y);
                Return(ref z);
                Return(ref r);
                Return(ref g);
                Return(ref b);
                Return(ref intensity);
                Return(ref classification);
                Return(ref time);
                count = 0;
            }

            private static void Return<T>(ref T[] arr)
            {
                if (arr == null) return;
                ArrayPool<T>.Shared.Return(arr, clearArray: true);
                arr = null;
            }

            public void WriteToFiles(string fullpath, int cellX, int cellY, int cellZ, (int x, int y, int z) key, PCROOT self, bool firstWrite)
            {
                int[] order = null;

                try
                {
                    // Optional chunk randomization: one order used for pct + rgb + int + cla
                    if (self.importSettings.randomize && count > 1)
                    {
                        order = ArrayPool<int>.Shared.Rent(count);
                        for (int i = 0; i < count; i++) order[i] = i;

                        int seed = HashKey(key.x, key.y, key.z) ^ count;
                        var rnd = new Random(seed);

                        for (int i = count - 1; i > 0; i--)
                        {
                            int j = rnd.Next(i + 1);
                            int tmp = order[i];
                            order[i] = order[j];
                            order[j] = tmp;
                        }
                    }

                    var mode = firstWrite ? FileMode.Create : FileMode.Append;

                    // Append points (.pct)
                    using (var writerPoints = new BinaryWriter(new BufferedStream(
                        new FileStream(fullpath, mode, FileAccess.Write, FileShare.Read))))
                    {
                        if (order == null)
                        {
                            for (int i = 0; i < count; i++)
                                WriteOnePoint(i, writerPoints, cellX, cellY, cellZ, self);
                        }
                        else
                        {
                            for (int k = 0; k < count; k++)
                                WriteOnePoint(order[k], writerPoints, cellX, cellY, cellZ, self);
                        }
                    }

                    // Separate outputs when not packed
                    if (!self.importSettings.packColors && !useLossyFiltering)
                    {
                        // .rgb
                        using (var writerColors = new BinaryWriter(new BufferedStream(
                            new FileStream(fullpath + ".rgb", mode, FileAccess.Write, FileShare.Read))))
                        {
                            if (order == null)
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    self.FloatToBytes(r[i], self.colorBuffer, 0);
                                    self.FloatToBytes(g[i], self.colorBuffer, 4);
                                    self.FloatToBytes(b[i], self.colorBuffer, 8);
                                    writerColors.Write(self.colorBuffer);
                                }
                            }
                            else
                            {
                                for (int k = 0; k < count; k++)
                                {
                                    int i = order[k];
                                    self.FloatToBytes(r[i], self.colorBuffer, 0);
                                    self.FloatToBytes(g[i], self.colorBuffer, 4);
                                    self.FloatToBytes(b[i], self.colorBuffer, 8);
                                    writerColors.Write(self.colorBuffer);
                                }
                            }
                        }

                        // .int
                        if (self.importSettings.importRGB && self.importSettings.importIntensity)
                        {
                            using var writerIntensity = new BinaryWriter(new BufferedStream(
                                new FileStream(fullpath + ".int", mode, FileAccess.Write, FileShare.Read)));

                            if (order == null)
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    float c = NormalizeIntensity(intensity[i], self.importSettings);
                                    writerIntensity.Write(c);
                                    writerIntensity.Write(c);
                                    writerIntensity.Write(c);
                                }
                            }
                            else
                            {
                                for (int k = 0; k < count; k++)
                                {
                                    int i = order[k];
                                    float c = NormalizeIntensity(intensity[i], self.importSettings);
                                    writerIntensity.Write(c);
                                    writerIntensity.Write(c);
                                    writerIntensity.Write(c);
                                }
                            }
                        }

                        // .cla
                        if (self.importSettings.importRGB && self.importSettings.importClassification)
                        {
                            using var writerClassification = new BinaryWriter(new BufferedStream(
                                new FileStream(fullpath + ".cla", mode, FileAccess.Write, FileShare.Read)));

                            if (order == null)
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    float c = classification[i] / 255f;
                                    writerClassification.Write(c);
                                    writerClassification.Write(c);
                                    writerClassification.Write(c);
                                }
                            }
                            else
                            {
                                for (int k = 0; k < count; k++)
                                {
                                    int i = order[k];
                                    float c = classification[i] / 255f;
                                    writerClassification.Write(c);
                                    writerClassification.Write(c);
                                    writerClassification.Write(c);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (order != null)
                        ArrayPool<int>.Shared.Return(order, clearArray: true);
                }
            }

            private void WriteOnePoint(int i, BinaryWriter writerPoints, int cellX, int cellY, int cellZ, PCROOT self)
            {
                float px = x[i];
                float py = y[i];
                float pz = z[i];

                int packedX = 0;
                int packedY = 0;

                if (self.importSettings.packColors)
                {
                    px -= (cellX * self.importSettings.gridSize);
                    py -= (cellY * self.importSettings.gridSize);
                    pz -= (cellZ * self.importSettings.gridSize);

                    if (self.importSettings.importRGB && self.importSettings.importIntensity && !self.importSettings.importClassification)
                    {
                        float c = py;
                        int cIntegral = (int)c;
                        int cFractional = (int)((c - cIntegral) * 255);
                        byte bg = (byte)(g[i] * 255);
                        byte bi = self.importSettings.useCustomIntensityRange ? (byte)(intensity[i] / 257) : (byte)intensity[i];
                        packedY = (bg << 24) | (bi << 16) | (cIntegral << 8) | cFractional;
                    }
                    else if (self.importSettings.importRGB && !self.importSettings.importIntensity && self.importSettings.importClassification)
                    {
                        float c = py;
                        int cIntegral = (int)c;
                        int cFractional = (int)((c - cIntegral) * 255);
                        byte bg = (byte)(g[i] * 255);
                        byte bc = classification[i];
                        packedY = (bg << 24) | (bc << 16) | (cIntegral << 8) | cFractional;
                    }
                    else if (self.importSettings.importRGB && self.importSettings.importIntensity && self.importSettings.importClassification)
                    {
                        float c = py;
                        int cIntegral = (int)c;
                        int cFractional = (int)((c - cIntegral) * 255);
                        byte bg = (byte)(g[i] * 255);
                        byte bi = self.importSettings.useCustomIntensityRange ? (byte)(intensity[i] / 257) : (byte)intensity[i];
                        packedY = (bg << 24) | (bi << 16) | (cIntegral << 8) | cFractional;
                    }
                    else
                    {
                        py = Tools.SuperPacker(g[i] * 0.98f, py, self.importSettings.gridSize * self.importSettings.packMagicValue);
                    }

                    if (self.importSettings.importRGB && self.importSettings.importIntensity && self.importSettings.importClassification)
                    {
                        float c = px;
                        int cIntegral = (int)c;
                        int cFractional = (int)((c - cIntegral) * 255);
                        byte br = (byte)(r[i] * 255);
                        byte bc = classification[i];
                        packedX = (br << 24) | (bc << 16) | (cIntegral << 8) | cFractional;
                    }
                    else
                    {
                        px = Tools.SuperPacker(r[i] * 0.98f, px, self.importSettings.gridSize * self.importSettings.packMagicValue);
                    }

                    pz = Tools.SuperPacker(b[i] * 0.98f, pz, self.importSettings.gridSize * self.importSettings.packMagicValue);
                }

                if (self.importSettings.packColors && self.importSettings.importRGB && self.importSettings.importIntensity && self.importSettings.importClassification)
                    self.IntToBytes(packedX, self.pointBuffer, 0);
                else
                    self.FloatToBytes(px, self.pointBuffer, 0);

                if (self.importSettings.packColors && self.importSettings.importRGB && (self.importSettings.importIntensity || self.importSettings.importClassification))
                    self.IntToBytes(packedY, self.pointBuffer, 4);
                else
                    self.FloatToBytes(py, self.pointBuffer, 4);

                self.FloatToBytes(pz, self.pointBuffer, 8);
                writerPoints.Write(self.pointBuffer);
            } // WriteOnePoint
        } // class TileBuffer

    } // class
} // namespace
