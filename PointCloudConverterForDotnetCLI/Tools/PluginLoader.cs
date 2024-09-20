using PointCloudConverterForDotnetCLI.Logger;
using PointCloudConverterForDotnetCLI.Writers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PointCloudConverterForDotnetCLI.Plugins
{
    public static class PluginLoader
    {
        static readonly string pluginDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Plugins");

        // TODO add logger, if needed
        //static ILogger Log;

        public static IWriter LoadWriter(string pluginName)
        {
            //Log = logger;

            string pluginPath = Path.Combine(pluginDirectory, pluginName + ".dll");
            //Log.Write($"Loading plugin at {pluginPath}");
            if (File.Exists(pluginPath) == false) throw new FileNotFoundException($"The plugin at {pluginPath} could not be found.");

            // Load the plugin assembly
            var pluginAssembly = Assembly.LoadFrom(pluginPath);

            // Find the specific type 'PointCloudConverter.Writers.GLTF'
            var writerType = pluginAssembly.GetType("PointCloudConverter.Writers.GLTF");

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
