#include <openmedia/plugin/SandboxedPlugin.h>
#include <openmedia/ipc/CommandTypes.h>
#include <iostream>
#include <thread>
#include <chrono>

#ifdef _WIN32
#include <windows.h>
#endif

namespace openmedia::plugin {

SandboxedPlugin::SandboxedPlugin(const std::string& pluginPath, const std::string& pipeName)
    : m_pluginPath(pluginPath), m_pipeName(pipeName) {
    m_info.name = "SandboxedPlugin";
    m_info.displayName = "Sandboxed Plugin Wrapper";
    m_info.description = "Runs a plugin in a separate process for crash isolation.";
    m_info.apiVersion = OME_PLUGIN_API_VERSION;
    m_info.capabilities = PluginCapability::VideoFilter;

    // Configure IPC client to use the specified pipe
    m_clientConfig.pipeConfig.pipeName = "\\\\.\\pipe\\" + pipeName;
    m_clientConfig.autoLaunchServer = false; // We launch the host process manually
    m_clientConfig.connectionTimeoutMs = 5000;
}

SandboxedPlugin::~SandboxedPlugin() {
    Shutdown();
}

void SandboxedPlugin::RestartSandbox() {
    std::cout << "[SandboxedPlugin] Starting host process for " << m_pluginPath << "\n";

    std::string cmd = "ome_plugin_host " + m_pipeName + " " + m_pluginPath;
#ifdef _WIN32
    // Launch detached process on Windows
    STARTUPINFOA si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));

    // Convert to mutable string for CreateProcessA
    char cmdBuf[512];
    strncpy_s(cmdBuf, cmd.c_str(), sizeof(cmdBuf));

    if (CreateProcessA(NULL, cmdBuf, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) {
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
    } else {
        std::cerr << "[SandboxedPlugin] Failed to create process\n";
    }
#else
    // Linux detached process
    std::string fullCmd = cmd + " &";
    system(fullCmd.c_str());
#endif

    // Wait a bit for the host to spin up
    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    m_client = std::make_unique<ipc::IPCClient>(m_clientConfig);

    auto result = m_client->Connect();
    if (result.has_value()) {
        std::cout << "[SandboxedPlugin] Connected to plugin host.\n";
    } else {
        std::cerr << "[SandboxedPlugin] Failed to connect to plugin host.\n";
    }
}

bool SandboxedPlugin::Initialize() {
    RestartSandbox();
    return m_client && m_client->IsConnected();
}

void SandboxedPlugin::Shutdown() {
    if (m_client && m_client->IsConnected()) {
        try {
            (void)m_client->SendCommand(ipc::CommandType::Shutdown, {});
        } catch (...) {}
    }
    m_client.reset();
}

const PluginInfo& SandboxedPlugin::GetInfo() const {
    return m_info;
}

bool SandboxedPlugin::Configure(const char* jsonConfig) {
    if (!m_client || !m_client->IsConnected()) return false;
    std::string configStr(jsonConfig ? jsonConfig : "");
    std::vector<uint8_t> payload(configStr.begin(), configStr.end());
    try {
        auto result = m_client->SendCommand(ipc::CommandType::ConfigurePlugin, payload);
        return result.has_value();
    } catch (...) {
        return false;
    }
}

const char* SandboxedPlugin::GetDefaultConfig() const {
    return "{}";
}

bool SandboxedPlugin::Setup(const VideoFilterParams& params) {
    m_params = params;
    // Real implementation would serialize params and send to host
    return true;
}

bool SandboxedPlugin::ProcessFrame(const core::MediaFrame& /*input*/, core::MediaFrame& /*output*/) {
    if (!m_client || !m_client->IsConnected()) {
        std::cout << "[SandboxedPlugin] Host disconnected! Initiating crash recovery...\n";
        RestartSandbox();
        Setup(m_params); // Re-initialize state
    }

    if (!m_client || !m_client->IsConnected()) return false;

    try {
        // Send frame info via IPC (in a full implementation, we'd copy data to shared memory first)
        std::vector<uint8_t> payload = {1, 2, 3}; // Stub payload
        auto response = m_client->SendCommand(
            ipc::CommandType::PluginProcessFrame, payload,
            std::chrono::milliseconds(5000));
        return response.has_value();
    } catch (const std::exception& e) {
        std::cerr << "[SandboxedPlugin] IPC Error during ProcessFrame: " << e.what() << "\n";
        m_client->Disconnect();
        return false;
    }
}

bool SandboxedPlugin::ProcessFrameGPU(const void* /*inputTexture*/, void* /*outputTexture*/, int /*textureFormat*/) {
    return false;
}

VideoFilterParams SandboxedPlugin::GetOutputParams() const {
    return m_params;
}

void SandboxedPlugin::Reset() {
}

} // namespace openmedia::plugin
