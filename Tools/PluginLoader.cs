using PointCloudConverter.Logger;
using PointCloudConverter.Writers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PointCloudConverter.Plugins
{
    public static class PluginLoader
    {
        // Resolve plugin folder relative to the .exe location instead of current working directory
        static readonly string pluginDirectory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Plugins");

        // TODO add logger, if needed
        // static ILogger Log;

        public static IWriter LoadWriter(string pluginName)
        {
            // Log = logger;

            // Build the full path to the plugin DLL
            string pluginPath = Path.Combine(pluginDirectory, pluginName + ".dll");

            // Log.Write($"Loading plugin at {pluginPath}");

            // Check if the plugin DLL exists
            if (File.Exists(pluginPath) == false)
                throw new FileNotFoundException($"The plugin at {pluginPath} could not be found.");

            // Load the plugin assembly from the DLL
            var pluginAssembly = Assembly.LoadFrom(pluginPath);

            // Find the specific type 'PointCloudConverter.Writers.<PluginName>'
            // This assumes the type name inside the DLL matches the filename
            var writerType = pluginAssembly.GetType("PointCloudConverter.Writers." + pluginName);

            if (writerType == null)
                throw new InvalidOperationException($"No valid implementation of IWriter found in {pluginPath}");

            // Check if the type implements IWriter
            if (!typeof(IWriter).IsAssignableFrom(writerType))
                throw new InvalidOperationException($"{writerType.FullName} does not implement IWriter");

            // Create an instance of the IWriter implementation
            return (IWriter)Activator.CreateInstance(writerType);
        }
    }
}
