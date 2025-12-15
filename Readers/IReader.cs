using PointCloudConverter.Structs;
using Color = PointCloudConverter.Structs.Color;

namespace PointCloudConverter.Readers
{
    public interface IReader
    {
        // open filestream
        bool InitReader(ImportSettings importSettings, int fileIndex);
        // returns total point count, this is required to correctly read all points
        long GetPointCount();
        // bounds are used for AutoOffset
        Bounds GetBounds();
        // retrieve single point X,Y,Z coordinates (float)
        bool GetXYZ(out double x, out double y, out double z);
        // retrieve single point R,G,B colors (byte 0-255)
        void GetRGB(out float r, out float g, out float b);
        // retrieve single point scan time
        double GetTime();

        void Close();
        ushort GetIntensity();
        byte GetClassification();
        LasHeader GetMetaData(ImportSettings importSettings, int fileIndex);

        void Dispose();
    }
}
