using System;
using System.IO;

namespace OpenMedia.SDK
{
    public class Engine
    {
        public static bool Initialize(string configJson)
        {
            ConfigureLogDirectory();
            return NativeBridge.ome_engine_init(configJson);
        }

        /// <summary>
        /// Redirects native log output to %LocalAppData%\OpenMedia\logs
        /// to prevent SEHException in MSIX packaged apps where the default
        /// CWD-relative path ("./logs") points to a restricted system directory.
        /// Skips if OME_LOG_DIR is already configured by the consumer.
        /// </summary>
        private static void ConfigureLogDirectory()
        {
            if (Environment.GetEnvironmentVariable("OME_LOG_DIR") != null)
                return;

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenMedia", "logs");

            Environment.SetEnvironmentVariable("OME_LOG_DIR", logDir);
        }

        public static void Shutdown()
        {
            NativeBridge.ome_engine_shutdown();
        }
    }
}
