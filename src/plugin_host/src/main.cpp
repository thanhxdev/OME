#include <iostream>
#include <string>
#include <openmedia/plugin/PluginManager.h>
#include <openmedia/ipc/IPCServer.h>
#include <openmedia/ipc/CommandTypes.h>
#include <openmedia/plugin/IVideoFilter.h>
#include <thread>
#include <chrono>

using namespace openmedia::ipc;
using namespace openmedia::plugin;

int main(int argc, char** argv) {
    if (argc < 3) {
        std::cerr << "Usage: ome_plugin_host <pipe_name> <plugin_dll_path>\n";
        return 1;
    }

    std::string pipeName = argv[1];
    std::string pluginPath = argv[2];

    std::cout << "[ome_plugin_host] Starting... Pipe: " << pipeName << ", Plugin: " << pluginPath << "\n";

    // PluginManager is in openmedia::core namespace
    auto& pm = openmedia::core::PluginManager::GetInstance();
    auto res = pm.LoadPlugin(pluginPath);
    if (!res.has_value()) {
        std::cerr << "[ome_plugin_host] Failed to load plugin: " << res.error().message << "\n";
        return 2;
    }

    // Since we only load one plugin per host process, grab the first one
    auto plugins = pm.GetPlugins();
    if (plugins.empty()) {
        std::cerr << "[ome_plugin_host] No plugin loaded\n";
        return 3;
    }

    std::shared_ptr<IPlugin> plugin = plugins[0];
    auto filter = std::dynamic_pointer_cast<IVideoFilter>(plugin);

    // Configure server with the specified pipe name
    IPCServerConfig serverConfig;
    serverConfig.pipeConfig.pipeName = "\\\\.\\pipe\\" + pipeName;

    IPCServer server(serverConfig);

    server.RegisterHandler(CommandType::ConfigurePlugin,
        [&](uint32_t /*clientId*/, const std::vector<uint8_t>& payload)
            -> openmedia::core::Result<std::vector<uint8_t>> {
        std::string configStr(payload.begin(), payload.end());
        plugin->Configure(configStr.c_str());
        return std::vector<uint8_t>();
    });

    server.RegisterHandler(CommandType::PluginProcessFrame,
        [&](uint32_t /*clientId*/, const std::vector<uint8_t>& /*payload*/)
            -> openmedia::core::Result<std::vector<uint8_t>> {
        if (!filter) {
            return std::unexpected(openmedia::core::Error{
                openmedia::core::ErrorCode::InvalidArgument,
                "Plugin is not an IVideoFilter"});
        }
        // Simplified: The payload could contain width, height, format, and shared memory name
        // The host would open the shared memory, process, and write back.
        // For now, this is a stub acknowledging the command.
        return std::vector<uint8_t>{1}; // success
    });

    server.RegisterHandler(CommandType::Shutdown,
        [&](uint32_t /*clientId*/, const std::vector<uint8_t>& /*payload*/)
            -> openmedia::core::Result<std::vector<uint8_t>> {
        std::cout << "[ome_plugin_host] Received shutdown command. Exiting...\n";
        server.Stop();
        return std::vector<uint8_t>();
    });

    auto startResult = server.Start();
    if (!startResult.has_value()) {
        std::cerr << "[ome_plugin_host] Failed to start server\n";
        return 4;
    }
    std::cout << "[ome_plugin_host] Ready and listening on " << pipeName << "\n";

    // Wait until server stops (via Shutdown command or disconnection)
    while (server.IsRunning()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    pm.UnloadAll();
    std::cout << "[ome_plugin_host] Exited gracefully.\n";
    return 0;
}
