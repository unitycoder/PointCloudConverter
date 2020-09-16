namespace PointCloudConverter
{
    public struct PointCloudTile
    {
        public float minX;
        public float minY;
        public float minZ;
        public float maxX;
        public float maxY;
        public float maxZ;

        public float centerX;
        public float centerY;
        public float centerZ;

        public int totalPoints;
        public int loadedPoints;
        public int visiblePoints;

        public string fileName;

        // cell min edge
        public int cellX;
        public int cellY;
        public int cellZ;

    }
}
