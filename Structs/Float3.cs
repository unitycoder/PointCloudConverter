namespace PointCloudConverter.Structs
{
    public struct Float3
    {
        public float x;
        public float y;
        public float z;
        public bool hasError;

        public override string ToString()
        {
            return $"{x}, {y}, {z} " + (hasError ? " (Error = True)" : "");
        }
    }
}
