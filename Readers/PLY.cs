using PointCloudConverter.Structs;
using Ply.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Color = PointCloudConverter.Structs.Color;
using System.Diagnostics;
using static Ply.Net.PlyParser;
using System.Collections.Immutable;

namespace PointCloudConverter.Readers
{
    public class PLY : IReader, IDisposable
    {
        private PlyParser.Dataset dataset;
        private int pointIndex;
        private int pointCount;

        private List<ElementData> vertexChunks;
        private int currentChunkIndex;
        private int currentPointInChunk;

        private PropertyData px, py, pz;
        private PropertyData pr, pg, pb;

        //private PlyParser.PropertyData pintensity, pclass, ptime;

        private Float3 currentPoint;
        private Color currentColor;
        //        private double currentTime;
        //        private byte currentIntensity;
        //        private byte currentClassification;
        private Bounds bounds;

        public bool InitReader(ImportSettings importSettings, int fileIndex)
        {
            var file = importSettings.inputFiles[fileIndex];

            using var stream = File.OpenRead(file);
            dataset = PlyParser.Parse(stream, 4096);

            vertexChunks = dataset.Data
                .Where(d => d.Element.Type == ElementType.Vertex)
                .ToList();

            if (vertexChunks.Count == 0) return false;

            pointCount = vertexChunks.Sum(chunk => ((Array)chunk.Data[0].Data).Length);
            currentChunkIndex = 0;
            currentPointInChunk = 0;

            SetCurrentChunkProperties(); // helper method to cache px, py, pz, etc.

            CalculateBounds();

            return true;

        }

        public int GetPointCount() => pointCount;

        public Bounds GetBounds() => bounds;

        public Float3 GetXYZ()
        {
            if (currentChunkIndex >= vertexChunks.Count)
                return new Float3 { hasError = true };

            int chunkSize = ((Array)px.Data).Length;
            if (currentPointInChunk >= chunkSize)
            {
                currentChunkIndex++;
                if (currentChunkIndex >= vertexChunks.Count)
                    return new Float3 { hasError = true };

                currentPointInChunk = 0;
                SetCurrentChunkProperties();
            }

            currentPoint = new Float3
            {
                x = Convert.ToSingle(px.Data.GetValue(currentPointInChunk)),
                y = Convert.ToSingle(py.Data.GetValue(currentPointInChunk)),
                z = Convert.ToSingle(pz.Data.GetValue(currentPointInChunk)),
                hasError = false
            };

            currentColor = new Color
            {
                r = Convert.ToSingle(Convert.ToByte(pr.Data.GetValue(currentPointInChunk))) / 255f,
                g = Convert.ToSingle(Convert.ToByte(pg.Data.GetValue(currentPointInChunk))) / 255f,
                b = Convert.ToSingle(Convert.ToByte(pb.Data.GetValue(currentPointInChunk))) / 255f
            };

            currentPointInChunk++;
            return currentPoint;
        }


        public Color GetRGB()
        {
            //currentColor = new Color();
            //currentColor.r = 255;
            //currentColor.g = 0;
            //currentColor.b = 0;
            return currentColor;
        }

        public double GetTime()
        {
            return 0.0;
        }

        public byte GetIntensity()
        {
            return 0;
        }

        public byte GetClassification()
        {
            return 0;
        }

        // TODO return ply data
        public LasHeader GetMetaData(ImportSettings importSettings, int fileIndex)
        {
            return new LasHeader
            {
                FileName = importSettings.inputFiles[fileIndex],
                NumberOfPointRecords = (uint)pointCount,
                MinX = bounds.minX,
                MaxX = bounds.maxX,
                MinY = bounds.minY,
                MaxY = bounds.maxY,
                MinZ = bounds.minZ,
                MaxZ = bounds.maxZ
            };
        }

        public void Close()
        {
            dataset = null;
        }

        public void Dispose() => Close();

        private void CalculateBounds()
        {
            bounds = new Bounds
            {
                minX = float.MaxValue,
                maxX = float.MinValue,
                minY = float.MaxValue,
                maxY = float.MinValue,
                minZ = float.MaxValue,
                maxZ = float.MinValue
            };

            foreach (var chunk in vertexChunks)
            {
                var cx = chunk["x"]!;
                var cy = chunk["y"]!;
                var cz = chunk["z"]!;
                int count = ((Array)cx.Data).Length;

                for (int i = 0; i < count; i++)
                {
                    float x = Convert.ToSingle(cx.Data.GetValue(i));
                    float y = Convert.ToSingle(cy.Data.GetValue(i));
                    float z = Convert.ToSingle(cz.Data.GetValue(i));

                    bounds.minX = Math.Min(bounds.minX, x);
                    bounds.maxX = Math.Max(bounds.maxX, x);
                    bounds.minY = Math.Min(bounds.minY, y);
                    bounds.maxY = Math.Max(bounds.maxY, y);
                    bounds.minZ = Math.Min(bounds.minZ, z);
                    bounds.maxZ = Math.Max(bounds.maxZ, z);
                }
            }
        }


        ushort IReader.GetIntensity()
        {
            return GetIntensity();
        }

        private void SetCurrentChunkProperties()
        {
            var chunk = vertexChunks[currentChunkIndex];
            px = chunk["x"] ?? throw new Exception("Missing 'x' property");
            py = chunk["y"] ?? throw new Exception("Missing 'y' property");
            pz = chunk["z"] ?? throw new Exception("Missing 'z' property");
            pr = chunk["red"] ?? throw new Exception("Missing 'red' property");
            pg = chunk["green"] ?? throw new Exception("Missing 'green' property");
            pb = chunk["blue"] ?? throw new Exception("Missing 'blue' property");
        }


    }
}
