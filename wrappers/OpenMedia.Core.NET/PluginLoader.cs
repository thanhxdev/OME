using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace OpenMedia.SDK
{
    public static class PluginLoader
    {
        private static List<IOpenMediaPlugin> _loadedPlugins = new List<IOpenMediaPlugin>();

        public static void LoadDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return;

            string[] dllFiles = Directory.GetFiles(directoryPath, "*.dll");
            foreach (var dll in dllFiles)
            {
                try
                {
                    // For a robust system, we would use AssemblyLoadContext, 
                    // but Assembly.LoadFrom is sufficient for this simple architecture
                    Assembly assembly = Assembly.LoadFrom(dll);
                    foreach (Type type in assembly.GetExportedTypes())
                    {
                        if (typeof(IOpenMediaPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                        {
                            var plugin = (IOpenMediaPlugin)Activator.CreateInstance(type);
                            if (plugin != null)
                            {
                                plugin.Initialize();
                                _loadedPlugins.Add(plugin);
                                Console.WriteLine($"[PluginLoader] Loaded managed plugin: {plugin.Name} v{plugin.Version}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PluginLoader] Error loading {dll}: {ex.Message}");
                }
            }
        }

        public static void ShutdownAll()
        {
            foreach (var plugin in _loadedPlugins)
            {
                try
                {
                    plugin.Shutdown();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PluginLoader] Error shutting down plugin {plugin.Name}: {ex.Message}");
                }
            }
            _loadedPlugins.Clear();
        }
    }
}
