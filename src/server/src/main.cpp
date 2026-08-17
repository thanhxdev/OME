/// @file main.cpp
/// @brief OpenMediaServer.exe entry point

#include <openmedia/server/ServerApp.h>
#include <openmedia/core/Logger.h>

#include <iostream>
#include <string>

int main(int argc, char* argv[]) {
    using namespace openmedia;

    // Parse command line
    server::ServerConfig config;

    for (int i = 1; i < argc; ++i) {
        std::string arg(argv[i]);

        if (arg == "--config" && i + 1 < argc) {
            config.configFile = argv[++i];
        } else if (arg == "--pipe-name" && i + 1 < argc) {
            config.ipcConfig.pipeConfig.pipeName = argv[++i];
        } else if (arg == "--workers" && i + 1 < argc) {
            config.workerConfig.threadCount = std::stoul(argv[++i]);
        } else if (arg == "--no-watchdog") {
            config.enableWatchdog = false;
        } else if (arg == "--service") {
            config.runAsService = true;
        } else if (arg == "--help" || arg == "-h") {
            std::cout << "OpenMediaServer v" << server::ServerApp::GetVersion() << "\n\n"
                      << "Usage: OpenMediaServer.exe [options]\n\n"
                      << "Options:\n"
                      << "  --config <path>       Server config file (JSON)\n"
                      << "  --pipe-name <name>    Named Pipe name (default: \\\\.\\pipe\\OpenMediaSDK)\n"
                      << "  --workers <count>     Worker thread count (default: auto)\n"
                      << "  --no-watchdog         Disable watchdog timer\n"
                      << "  --service             Run as Windows Service\n"
                      << "  --help, -h            Show this help\n";
            return 0;
        }
    }

    // Create and run server
    server::ServerApp app;

    auto initResult = app.Initialize(config);
    if (!initResult) {
        std::cerr << "Failed to initialize server: "
                  << initResult.error().message << "\n";
        return 1;
    }

    auto runResult = app.Run();
    if (!runResult) {
        std::cerr << "Server error: "
                  << runResult.error().message << "\n";
        return 1;
    }

    return 0;
}
