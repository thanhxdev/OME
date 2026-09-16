using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OpenMedia.Platform.Internal
{
    /// <summary>
    /// Discovers the path to <c>OpenMediaServer.exe</c> using a prioritized chain:
    /// <list type="number">
    ///   <item><description>Environment variable <c>OPENMEDIA_SERVER_PATH</c></description></item>
    ///   <item><description>App config / <c>appsettings.json</c></description></item>
    ///   <item><description>Windows Registry <c>HKLM\Software\OpenMedia\ServerPath</c></description></item>
    ///   <item><description>Default install path <c>%ProgramFiles%\OpenMedia\bin\OpenMediaServer.exe</c></description></item>
    /// </list>
    /// </summary>
    public static class ServerDiscovery
    {
        private const string EnvVarName = "OPENMEDIA_SERVER_PATH";
        private const string RegistryKey = @"Software\OpenMedia";
        private const string RegistryValue = "ServerPath";
        private const string ServerExecutable = "OpenMediaServer.exe";

        /// <summary>
        /// Discovers the server executable path. Returns <c>null</c> if not found.
        /// </summary>
        /// <param name="explicitPath">Optional explicit path that bypasses discovery.</param>
        /// <returns>Full path to <c>OpenMediaServer.exe</c>, or <c>null</c>.</returns>
        public static string? Discover(string? explicitPath = null)
        {
            // 0. Explicit path (from RuntimeOptions.ServerPath)
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                if (ValidateExecutable(explicitPath))
                {
                    Trace.WriteLine($"[OpenMedia.Platform] Discovery: Using explicit path: {explicitPath}");
                    return explicitPath;
                }
                Trace.WriteLine($"[OpenMedia.Platform] Discovery: Explicit path not valid: {explicitPath}");
            }

            // 1. Environment variable (Direct Server Path or SDK Directory)
            var envPath = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(envPath) && ValidateExecutable(envPath))
            {
                Trace.WriteLine($"[OpenMedia.Platform] Discovery: Found via env var {EnvVarName}: {envPath}");
                return envPath;
            }

            var sdkEnvDir = Environment.GetEnvironmentVariable("OPENMEDIA_SDK_DIR");
            if (!string.IsNullOrWhiteSpace(sdkEnvDir))
            {
                var candidateBin = Path.Combine(sdkEnvDir, "bin", ServerExecutable);
                if (ValidateExecutable(candidateBin))
                {
                    Trace.WriteLine($"[OpenMedia.Platform] Discovery: Found via OPENMEDIA_SDK_DIR (bin): {candidateBin}");
                    return candidateBin;
                }

                var candidateRoot = Path.Combine(sdkEnvDir, ServerExecutable);
                if (ValidateExecutable(candidateRoot))
                {
                    Trace.WriteLine($"[OpenMedia.Platform] Discovery: Found via OPENMEDIA_SDK_DIR (root): {candidateRoot}");
                    return candidateRoot;
                }
            }

            // 2. App directory (co-located server)
            var appDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ServerExecutable);
            if (ValidateExecutable(appDirPath))
            {
                Trace.WriteLine($"[OpenMedia.Platform] Discovery: Found in app directory: {appDirPath}");
                return appDirPath;
            }

            // 3. Windows Registry
            var registryPath = TryReadRegistry();
            if (registryPath != null && ValidateExecutable(registryPath))
            {
                Trace.WriteLine($"[OpenMedia.Platform] Discovery: Found via Registry: {registryPath}");
                return registryPath;
            }

            // 4. Default install path
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var sdkDefaultBin = Path.Combine(programFiles, "OpenMedia", "SDK", "bin", ServerExecutable);
            if (ValidateExecutable(sdkDefaultBin))
            {
                Trace.WriteLine($"[OpenMedia.Platform] Discovery: Found at default SDK path: {sdkDefaultBin}");
                return sdkDefaultBin;
            }

            var defaultPath = Path.Combine(programFiles, "OpenMedia", "bin", ServerExecutable);
            if (ValidateExecutable(defaultPath))
            {
                Trace.WriteLine($"[OpenMedia.Platform] Discovery: Found at default path: {defaultPath}");
                return defaultPath;
            }

            Trace.WriteLine("[OpenMedia.Platform] Discovery: Server not found in any location.");
            return null;
        }

        private static bool ValidateExecutable(string path)
        {
            return File.Exists(path) &&
                   Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryReadRegistry()
        {
            try
            {
                // 1. Try Software\OpenMedia\SDK (Modular Architecture)
                using (var sdkKey = Registry.LocalMachine.OpenSubKey(@"Software\OpenMedia\SDK"))
                {
                    if (sdkKey != null)
                    {
                        var sdkPath = sdkKey.GetValue("Path") as string;
                        if (!string.IsNullOrWhiteSpace(sdkPath))
                        {
                            var serverBin = Path.Combine(sdkPath, "bin", ServerExecutable);
                            if (ValidateExecutable(serverBin)) return serverBin;

                            var serverRoot = Path.Combine(sdkPath, ServerExecutable);
                            if (ValidateExecutable(serverRoot)) return serverRoot;
                        }
                    }
                }

                // 2. Try legacy Software\OpenMedia -> ServerPath
                using (var key = Registry.LocalMachine.OpenSubKey(RegistryKey))
                {
                    return key?.GetValue(RegistryValue) as string;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
