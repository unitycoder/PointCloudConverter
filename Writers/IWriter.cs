using PointCloudConverter.Structs;

namespace PointCloudConverter.Writers
{
    public interface IWriter
    {
        // create output filestream, called before looping through points
        bool InitWriter(ImportSettings importSettings, int pointCount);
        // optional: if need to create special file header
        void CreateHeader(int pointCount);
        // output point X,Y,Z values to file
        void WriteXYZ(float x, float y, float z);
        // output R,G,B values (float 0-1) to file
        void WriteRGB(float r, float g, float b);
        // optional: if you need to collect points for later processing
        void AddPoint(int index, float x, float y, float z, float r, float g, float b, bool hasIntensity, float i);
        // optional: randomizes points (to use dynamic resolution/tile LOD in Unity)
        void Randomize();
        // called after all points have been looped through
        void Save(int fileIndex); // saves and closes, TODO but have separate close also.., FIXME remove index, not used?
        // optional: cleanup temporary files
        void Cleanup(int fileIndex);
        // close filestream
        void Close();
    }
}

