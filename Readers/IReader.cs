using PointCloudConverter.Structs;

namespace PointCloudConverter.Readers
{
    public interface IReader
    {
        // open filestream
        bool InitReader(ImportSettings importSettings, int fileIndex);
        // returns total point count, this is required to correctly read all points
        int GetPointCount();
        // bounds are used for AutoOffset
        Bounds GetBounds();
        // retrieve single point X,Y,Z coordinates (float)
        Float3 GetXYZ();
        // retrieve single point R,G,B colors (byte 0-255)
        Color GetRGB();
        // close filestream
        void Close();
        Color GetIntensity();
    }
}
