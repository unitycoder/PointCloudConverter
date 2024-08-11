using PointCloudConverter.Logger;
using PointCloudConverter;
using System;
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

    public class LogText : ILogger
    {
        public void Write(string msg)
        {
            //Console.WriteLine(msg);
            Trace.WriteLine(msg);
        }

        public void Write(string msg, LogEvent eventType)
        {
            // TODO not supported yet (later could have different colors for different events)
            //Console.WriteLine("NOTIMPLEMENTED: "+msg);
            //throw new NotImplementedException();
        }
    }

    public class LogJSON : ILogger
    {
        public void Write(string msg)
        {
            // no output, since its not json message
        }

        public void Write(string msg, LogEvent eventType)
        {
            var json = msg;
            Console.WriteLine(json);
        }
    }

}

public static class Log
{
    static ILogger logger;
    static ImportSettings settings = null; // copy of settings for logging?
    public static string version = null;

    public static bool json()
    {
        if (settings == null) return false;
        return settings.useJSONLog;
    }

    //public static void CreateLogger(ImportSettings import, string version)
    public static void CreateLogger(bool isJSON, string version)
    {
        //if (settings != null) Console.WriteLine("Warning: CreateLogger has been called already.. Replacing it.");

        Log.version = version;

        if (isJSON == true)
        {
            logger = new LogJSON();
        }
        else
        {
            logger = new LogText();
        }
    }

    public static void SetSettings(ImportSettings import)
    {
        settings = import;
    }

    public static void WriteLine(string message)
    {
        // this is for console.writeline, no json
        logger.Write(message);
    }

    // this is for json
    public static void WriteLine(string message, LogEvent logEvent)
    {
        logger.Write(message, logEvent);
    }
}