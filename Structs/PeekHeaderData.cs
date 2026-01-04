using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PointCloudConverter.Structs
{
    public struct PeekHeaderData
    {
        public bool success;
        public float minX;
        public float minY;
        public float minZ;
        public long pointCount;
        public long fileSize;
    }
}
