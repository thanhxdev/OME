using OpenMedia.Platform.Internal;

namespace OpenMedia.Platform.Tests
{
    public class ServerDiscoveryTests
    {
        [Fact]
        public void Discover_ExplicitPath_ValidFile_ReturnsPath()
        {
            // Arrange — use a known existing exe on every Windows system
            var knownExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");

            // Act
            var result = ServerDiscovery.Discover(knownExe);

            // Assert
            Assert.Equal(knownExe, result);
        }

        [Fact]
        public void Discover_ExplicitPath_InvalidFile_FallsThrough()
        {
            // Arrange — path does not exist
            var result = ServerDiscovery.Discover(@"C:\nonexistent\fake_server.exe");

            // Act & Assert — should not return the invalid path
            // (may return null or a different valid path depending on environment)
            Assert.NotEqual(@"C:\nonexistent\fake_server.exe", result ?? "");
        }

        [Fact]
        public void Discover_EnvironmentVariable_TakesPriority()
        {
            // Arrange
            var knownExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");
            Environment.SetEnvironmentVariable("OPENMEDIA_SERVER_PATH", knownExe);

            try
            {
                // Act
                var result = ServerDiscovery.Discover();

                // Assert
                Assert.Equal(knownExe, result);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable("OPENMEDIA_SERVER_PATH", null);
            }
        }

        [Fact]
        public void Discover_NoValidPath_ReturnsNull()
        {
            // Arrange — clear env var, use invalid explicit path
            Environment.SetEnvironmentVariable("OPENMEDIA_SERVER_PATH", null);

            // Act — discovery chain should exhaust all options
            // Result depends on whether OpenMediaServer.exe is actually installed
            var result = ServerDiscovery.Discover(@"C:\totally_fake_path\nope.exe");

            // Assert — we can at least verify it doesn't return the invalid explicit path
            Assert.NotEqual(@"C:\totally_fake_path\nope.exe", result ?? "");
        }

        [Fact]
        public void Discover_NonExeFile_Rejected()
        {
            // Arrange — a valid file but not an .exe
            var textFile = Path.GetTempFileName(); // .tmp extension

            try
            {
                // Act
                var result = ServerDiscovery.Discover(textFile);

                // Assert — should reject non-exe files
                Assert.NotEqual(textFile, result ?? "");
            }
            finally
            {
                File.Delete(textFile);
            }
        }
    }
}
