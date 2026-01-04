using System.Text.Json.Serialization;

namespace PointCloudConverter.Structs.Metadata
{
    public class Job
    {
        public string ConverterVersion { get; set; }
        
        public ImportSettings ImportSettings { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Elapsed { get; internal set; }

        public long TotalPoints { get; set; }
        public long TotalFileSizeBytes { get; set; }
    }

    public class JobMetadata
    {
        [JsonPropertyOrder(0)]
        public Job Job { get; set; }
        public List<LasHeader> lasHeaders { get; set; } = new List<LasHeader>();
    }
}
