namespace PointCloudConverter.Structs
{
    public struct Double3
    {
        public double x;
        public double y;
        public double z;

        public bool hasError;

        public Double3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            hasError = false;
        }

        public override string ToString()
        {
            return $"{x}, {y}, {z} " + (hasError ? " (Error = True)" : "");
        }
    }
}
