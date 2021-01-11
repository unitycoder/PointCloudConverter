namespace PointCloudConverter.Structs
{
    public struct Float3
    {
        public double x;
        public double y;
        public double z;

        public bool hasError;

        public override string ToString()
        {
            return $"{x}, {y}, {z} " + (hasError ? " (Error = True)" : "");
        }
    }
}
