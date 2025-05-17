using PointCloudConverter.Structs;
using Ply.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Color = PointCloudConverter.Structs.Color;
using System.Diagnostics;

namespace PointCloudConverter.Readers
{
    public class PLY : IReader, IDisposable
    {
        private PlyParser.Dataset dataset;
        private int pointIndex;
        private int pointCount;

        private PlyParser.PropertyData px, py, pz;
        private PlyParser.PropertyData pr, pg, pb;
        //private PlyParser.PropertyData pintensity, pclass, ptime;

        private Float3 currentPoint;
        private Color currentColor;
        //        private double currentTime;
        //        private byte currentIntensity;
        //        private byte currentClassification;
        private Bounds bounds;


        //int? taskID;
        //// add constructor
        //public PLY(int? _taskID)
        //{
        //    taskID = _taskID;
        //}

        public bool InitReader(ImportSettings importSettings, int fileIndex)
        {
            var file = importSettings.inputFiles[fileIndex];
            using var stream = File.OpenRead(file);
            dataset = PlyParser.Parse(stream, 1024);

            //var info = PlyParser.ParseHeader(file);
            //var infoVertices = info.Elements.FirstOrDefault(x => x.Type == PlyParser.ElementType.Vertex);
            //Trace.WriteLine($"PLY: {file} has {infoVertices?.Count} vertices");

            var vertexElement = dataset.Data.FirstOrDefault(d => d.Element.Type == PlyParser.ElementType.Vertex);
            if (vertexElement == null) return false;

            pointCount = vertexElement.Data[0].Data.Length;

            px = vertexElement["x"] ?? throw new Exception("Missing 'x' property in PLY file");
            py = vertexElement["y"] ?? throw new Exception("Missing 'y' property in PLY file");
            pz = vertexElement["z"] ?? throw new Exception("Missing 'z' property in PLY file");

            pr = vertexElement["red"];
            pg = vertexElement["green"];
            pb = vertexElement["blue"];

            Debug.WriteLine($"PLY: {file} has {pointCount} points");
            Debug.WriteLine($"PLY: {file} has {pr.Data.Length} pr values");


            //pa = vertexElement["alpha"];
            //            pintensity = vertexElement["intensity"] ?? vertexElement["scalar_intensity"];
            //            pclass = vertexElement["classification"] ?? vertexElement["scalar_classification"];
            //            ptime = vertexElement["time"];

            CalculateBounds();
            pointIndex = 0;

            return true;
        }

        public int GetPointCount() => pointCount;

        public Bounds GetBounds() => bounds;

        public Float3 GetXYZ()
        {
            if (pointIndex >= pointCount)
                return new Float3 { hasError = true };

            currentPoint = new Float3
            {
                x = Convert.ToSingle(px.Data.GetValue(pointIndex)),
                y = Convert.ToSingle(py.Data.GetValue(pointIndex)),
                z = Convert.ToSingle(pz.Data.GetValue(pointIndex)),
                hasError = false
            };

            //Trace.WriteLine($"PLY: {pointIndex} {pr.Data.GetValue(pointIndex)} {pg.Data.GetValue(pointIndex)} {pb.Data.GetValue(pointIndex)}");
            currentColor = new Color
            {
                r = pr != null ? Convert.ToSingle(Convert.ToByte(pr.Data.GetValue(pointIndex))) / 255f : 1f,
                g = pg != null ? Convert.ToSingle(Convert.ToByte(pg.Data.GetValue(pointIndex))) / 255f : 1f,
                b = pb != null ? Convert.ToSingle(Convert.ToByte(pb.Data.GetValue(pointIndex))) / 255f : 1f
            };


            //Trace.WriteLine($"PLY: {pointIndex} {currentColor.r} {currentColor.g} {currentColor.b}");

            //            currentIntensity = pintensity != null ? Convert.ToByte(pintensity.Data.GetValue(pointIndex)) : (byte)0;
            //            currentClassification = pclass != null ? Convert.ToByte(pclass.Data.GetValue(pointIndex)) : (byte)0;
            //            currentTime = ptime != null ? Convert.ToDouble(ptime.Data.GetValue(pointIndex)) : 0.0;

            pointIndex++;
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
            // NOTE doesnt support BINARY ply

            // need to calculate manually
            bounds = new Bounds
            {
                minX = float.MaxValue,
                maxX = float.MinValue,
                minY = float.MaxValue,
                maxY = float.MinValue,
                minZ = float.MaxValue,
                maxZ = float.MinValue
            };

            for (int i = 0; i < pointCount; i++)
            {
                float x = Convert.ToSingle(px.Data.GetValue(i));
                float y = Convert.ToSingle(py.Data.GetValue(i));
                float z = Convert.ToSingle(pz.Data.GetValue(i));

                bounds.minX = Math.Min(bounds.minX, x);
                bounds.maxX = Math.Max(bounds.maxX, x);
                bounds.minY = Math.Min(bounds.minY, y);
                bounds.maxY = Math.Max(bounds.maxY, y);
                bounds.minZ = Math.Min(bounds.minZ, z);
                bounds.maxZ = Math.Max(bounds.maxZ, z);
            }
        }
    }
}
