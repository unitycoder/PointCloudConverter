using System.Diagnostics;

namespace PointCloudConverter.Logger
{
    public enum LogEvent
    {
        Start,
        Settings,
        File,
        End,
        Error,
        Warning,
        Info,
        Progress,
        Debug
    }

    public enum LogStatus
    {
        Processing,
        Complete
    }

    public interface ILogger
    {
        void Write(string msg);
        void Write(string msg, LogEvent eventType);
    }

    // Handles non-JSON (text-based) logging
    public class LogText : ILogger
    {
        public void Write(string msg)
        {
            Console.WriteLine(msg);
            Trace.WriteLine(msg);
        }

        public void Write(string msg, LogEvent eventType)
        {
            // Could be expanded to handle different events in the future
            //Console.WriteLine($"{eventType}: {msg}");
        }
    }

    // Handles JSON-based logging
    public class LogJSON : ILogger
    {
        public void Write(string msg)
        {
            //Console.WriteLine(msg);
        }

        public void Write(string msg, LogEvent eventType)
        {
            Console.WriteLine(msg);
        }
    }

    public static class LoggerFactory
    {
        public static ILogger CreateLogger(bool isJSON)
        {
            Trace.WriteLine($"Creating logger with JSON: {isJSON}");
            if (isJSON)
            {
                return new LogJSON();
            }
            else
            {
                return new LogText();
            }
        }
    }
}
