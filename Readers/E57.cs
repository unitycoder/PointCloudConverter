using System;
using System.IO;
using System.Collections.Generic;
using Aardvark.Base;
using Aardvark.Data.Points.Import;
using PointCloudConverter.Structs;
using static Aardvark.Data.Points.Import.E57;
using Aardvark.Data.Points;
using System.Text.Json;
using Aardvark.Data.E57;

namespace PointCloudConverter.Readers
{
    public class E57 : IReader, IDisposable
    {
        private IEnumerator<E57Chunk> chunkEnumerator;
        private E57Chunk currentChunk;
        private int currentPointIndex = 0;

        private ASTM_E57.E57FileHeader header;
        private E57MetaData metaData;

        private Float3 lastXYZ;

        public struct E57MetaData
        {
            public string Name { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
            public double RX { get; set; }
            public double RY { get; set; }
            public double RZ { get; set; }
            public double RW { get; set; }
        }

        public bool InitReader(ImportSettings importSettings, int fileIndex)
        {
            try
            {
                var filePath = importSettings.inputFiles[fileIndex];

                // Read header metadata
                using var stream = File.OpenRead(filePath);
                header = ASTM_E57.E57FileHeader.Parse(stream, new FileInfo(filePath).Length, false);
                stream.Close();

                var data3D = header.E57Root.Data3D[0];
                var pose = data3D.Pose;

                metaData = new E57MetaData
                {
                    Name = data3D.Name
                };

                if (pose != null)
                {
                    metaData.X = pose.Translation.X;
                    metaData.Y = importSettings.swapYZ ? pose.Translation.Z : pose.Translation.Y;
                    metaData.Z = importSettings.swapYZ ? pose.Translation.Y : pose.Translation.Z;

                    metaData.RX = pose.Rotation.X;
                    metaData.RY = importSettings.swapYZ ? pose.Rotation.Z : pose.Rotation.Y;
                    metaData.RZ = importSettings.swapYZ ? pose.Rotation.Y : pose.Rotation.Z;
                    metaData.RW = pose.Rotation.W;
                }
                else
                {
                    // Optional: assign default values or log warning
                    metaData.X = metaData.Y = metaData.Z = 0;
                    metaData.RX = metaData.RY = metaData.RZ = 0;
                    metaData.RW = 1;

                    Console.WriteLine("Warning: E57 scan pose is null.");
                }


                var chunks = ChunksFull(filePath, ParseConfig.Default);
                chunkEnumerator = chunks.GetEnumerator();

                if (!chunkEnumerator.MoveNext())
                    return false;

                currentChunk = chunkEnumerator.Current;
                currentPointIndex = 0;

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("E57 InitReader error: " + ex.Message);
                return false;
            }
        }

        public LasHeader GetMetaData(ImportSettings importSettings, int fileIndex)
        {
            return new LasHeader
            {
                FileName = importSettings.inputFiles[fileIndex],
                NumberOfPointRecords = (uint)(header?.E57Root?.Data3D?[0]?.Points?.RecordCount ?? 0)
            };
        }

        public Bounds GetBounds()
        {
            var bounds = header.E57Root.Data3D[0].CartesianBounds.Bounds;

            return new Bounds
            {
                minX = (float)bounds.X.Min,
                maxX = (float)bounds.X.Max,
                minY = (float)bounds.Y.Min,
                maxY = (float)bounds.Y.Max,
                minZ = (float)bounds.Z.Min,
                maxZ = (float)bounds.Z.Max
            };
        }

        public int GetPointCount()
        {
            return (int)(header?.E57Root?.Data3D?[0]?.Points?.RecordCount ?? 0);
        }

        public Float3 GetXYZ()
        {
            if (currentChunk == null || currentPointIndex >= currentChunk.Count)
            {
                if (!chunkEnumerator.MoveNext())
                    return new Float3 { hasError = true };

                currentChunk = chunkEnumerator.Current;
                currentPointIndex = 0;

                // clear cachedColors when chunk changes
                cachedColors = null;
            }

            var p = currentChunk.Positions[currentPointIndex];
            lastXYZ.x = p.X;
            lastXYZ.y = p.Y;
            lastXYZ.z = p.Z;
            lastXYZ.hasError = false;

            currentPointIndex++;
            return lastXYZ;
        }

        private C3b[] cachedColors = null;

        public Color GetRGB()
        {
            if (cachedColors == null && currentChunk?.Colors != null)
            {
                cachedColors = currentChunk.Colors;
            }

            int i = currentPointIndex - 1;

            if (cachedColors != null && i >= 0 && i < cachedColors.Length)
            {
                var c = cachedColors[i];
                return new Color
                {
                    r = c.R / 255f,
                    g = c.G / 255f,
                    b = c.B / 255f
                };
            }

            return default;
        }

        public ushort GetIntensity()
        {
            var i = currentPointIndex - 1;
            if (currentChunk?.Intensities != null && i >= 0 && i < currentChunk.Intensities.Length)
            {
                return (byte)currentChunk.Intensities[i];
            }
            return 0;
        }

        public byte GetClassification() => 0;

        public double GetTime()
        {
            // Not implemented for now
            return 0;
        }

        public void Close() { }

        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }

        ~E57()
        {
            Dispose();
        }

        public string GetMetaDataJSON()
        {
            return JsonSerializer.Serialize(metaData);
        }
    }
}
