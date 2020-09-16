namespace PointCloudConverter.Structs
{
    public struct Bounds
    {
        public float minX;
        public float minY;
        public float minZ;
        public float maxX;
        public float maxY;
        public float maxZ;
        // TODO add center

        public override string ToString()
        {
            return $"{minX}, {minY}, {minZ}, {maxX}, {maxY}, {maxZ}";
        }
    }
}
