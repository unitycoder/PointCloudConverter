using System.Diagnostics;

namespace PointCloudConverter.Logger
{
    public static class Log
    {
        private static ILogger logger;
        public static string version = null;
        public static bool isJSON = false;

        public static bool json()
        {
            return isJSON;
        }

        //// Create a logger based on whether JSON output is needed
        //public static void CreateLogger(bool isJSON, string version)
        //{
        //    Log.version = version;
        //    logger = LoggerFactory.CreateLogger(isJSON);
        //}

        public static void SetSettings(bool _isJSON)
        {
            Console.WriteLine($"Setting JSON to {_isJSON}");
            Trace.WriteLine($"Setting JSON to {_isJSON}");
            isJSON = _isJSON;
        }

        public static void WriteLine(string message)
        {
            logger.Write(message);
        }

        public static void WriteLine(string message, LogEvent logEvent)
        {
            logger.Write(message, logEvent);
        }
    }
}