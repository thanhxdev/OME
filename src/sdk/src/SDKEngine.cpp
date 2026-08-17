/// @file SDKEngine.cpp
/// @brief Client-side engine proxy — main SDK entry point implementation

#include <openmedia/sdk/SDKEngine.h>
#include <openmedia/ipc/IPCClient.h>
#include <openmedia/ipc/CommandMessage.h>
#include <openmedia/core/Logger.h>

#include <atomic>
#include <filesystem>
#include <mutex>

#ifdef _WIN32
#include <windows.h>
#include <tlhelp32.h>
#endif

namespace openmedia::sdk {

namespace {
auto& Log() { return core::Logger::Get("sdk.engine"); }

#ifdef _WIN32
bool IsProcessRunning(const std::string& processName) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return false;

    PROCESSENTRY32 pe = {};
    pe.dwSize = sizeof(pe);

    std::wstring wProcessName;
    wProcessName.assign(processName.begin(), processName.end());

    bool found = false;
    if (Process32First(snap, &pe)) {
        do {
            std::wstring name(pe.szExeFile);
            if (name == wProcessName) {
                found = true;
                break;
            }
        } while (Process32Next(snap, &pe));
    }

    CloseHandle(snap);
    return found;
}
#endif

} // anonymous namespace

struct SDKEngine::Impl {
    SDKConfig config;
    std::unique_ptr<ipc::IPCClient> client;
    std::atomic<ServerConnectionState> connectionState{ServerConnectionState::Disconnected};
    uint32_t nextPipelineId = 1;

    std::function<void(ServerConnectionState)> onConnectionChanged;
    std::mutex callbackMutex;

    Impl(const SDKConfig& cfg) : config(cfg) {}
};

SDKEngine::SDKEngine(const SDKConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {}

SDKEngine::~SDKEngine() {
    Disconnect();
}

std::unique_ptr<SDKEngine> SDKEngine::Create(const SDKConfig& config) {
    return std::unique_ptr<SDKEngine>(new SDKEngine(config));
}

core::VoidResult SDKEngine::Connect() {
    m_impl->connectionState = ServerConnectionState::Connecting;

    // Auto-launch server if configured
    if (m_impl->config.autoLaunchServer && !IsServerRunning()) {
        Log().Info("Server not running, launching {}...", m_impl->config.serverExePath);

#ifdef _WIN32
        STARTUPINFOA si = {};
        si.cb = sizeof(si);
        PROCESS_INFORMATION pi = {};

        std::string cmdLine = m_impl->config.serverExePath;
        if (!m_impl->config.pipeName.empty()) {
            cmdLine += " --pipe-name " + m_impl->config.pipeName;
        }

        BOOL created = CreateProcessA(
            nullptr, cmdLine.data(),
            nullptr, nullptr, FALSE,
            CREATE_NEW_PROCESS_GROUP,
            nullptr, nullptr, &si, &pi);

        if (!created) {
            m_impl->connectionState = ServerConnectionState::Error;
            return std::unexpected(core::Error{
                core::ErrorCode::IPCServerNotRunning,
                "Failed to launch server: " + m_impl->config.serverExePath,
                "SDKEngine"});
        }

        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);

        // Wait for server to start
        Sleep(1000);
#endif
    }

    // Create IPC client
    ipc::IPCClientConfig ipcConfig;
    if (!m_impl->config.pipeName.empty()) {
        ipcConfig.pipeConfig.pipeName = m_impl->config.pipeName;
    }
    ipcConfig.connectionTimeoutMs = m_impl->config.connectionTimeoutMs;
    ipcConfig.maxReconnectAttempts = m_impl->config.maxReconnectAttempts;
    ipcConfig.reconnectDelayMs = m_impl->config.reconnectDelayMs;
    ipcConfig.autoLaunchServer = false;  // We handle launch above

    m_impl->client = std::make_unique<ipc::IPCClient>(ipcConfig);

    auto result = m_impl->client->Connect();
    if (!result) {
        m_impl->connectionState = ServerConnectionState::Error;
        return std::unexpected(result.error());
    }

    // Handshake
    ipc::MessageBuilder builder;
    builder.WriteString(GetSDKVersion());

    auto handshakeResult = m_impl->client->SendCommand(
        ipc::CommandType::Handshake, builder.Finish());
    if (!handshakeResult) {
        m_impl->connectionState = ServerConnectionState::Error;
        return std::unexpected(handshakeResult.error());
    }

    m_impl->connectionState = ServerConnectionState::Connected;
    Log().Info("Connected to OpenMediaServer");

    {
        std::lock_guard lock(m_impl->callbackMutex);
        if (m_impl->onConnectionChanged) {
            m_impl->onConnectionChanged(ServerConnectionState::Connected);
        }
    }

    return {};
}

void SDKEngine::Disconnect() {
    if (m_impl->client && m_impl->client->IsConnected()) {
        m_impl->client->Disconnect();
    }
    m_impl->client.reset();
    m_impl->connectionState = ServerConnectionState::Disconnected;

    {
        std::lock_guard lock(m_impl->callbackMutex);
        if (m_impl->onConnectionChanged) {
            m_impl->onConnectionChanged(ServerConnectionState::Disconnected);
        }
    }
}

bool SDKEngine::IsConnected() const {
    return m_impl->client && m_impl->client->IsConnected();
}

ServerConnectionState SDKEngine::GetConnectionState() const {
    return m_impl->connectionState.load();
}

core::Result<std::unique_ptr<SDKPipeline>> SDKEngine::CreatePipeline(
    const PipelineConfig& config) {

    if (!IsConnected()) {
        return std::unexpected(core::Error{
            core::ErrorCode::IPCConnectionFailed,
            "Not connected to server",
            "SDKEngine"});
    }

    auto payload = ipc::BuildCreatePipelinePayload(
        config.name, config.width, config.height, config.frameRate);

    auto result = m_impl->client->SendCommand(
        ipc::CommandType::CreatePipeline, payload);
    if (!result) {
        return std::unexpected(result.error());
    }

    uint32_t pipelineId = ipc::ParseCreatePipelineResponse(result.value());
    if (pipelineId == 0) {
        pipelineId = m_impl->nextPipelineId++;
    }

    Log().Info("Created pipeline: {} (ID={})", config.name, pipelineId);

    return std::make_unique<SDKPipeline>(*m_impl->client, pipelineId);
}

core::Result<std::string> SDKEngine::GetServerVersion() const {
    if (!IsConnected()) {
        return std::unexpected(core::Error{
            core::ErrorCode::IPCConnectionFailed,
            "Not connected to server",
            "SDKEngine"});
    }

    auto result = m_impl->client->SendCommand(
        ipc::CommandType::GetStatus, {});
    if (!result) {
        return std::unexpected(result.error());
    }

    ipc::MessageReader reader(result.value());
    return reader.ReadString();
}

bool SDKEngine::IsServerRunning() const {
#ifdef _WIN32
    std::string exeName = std::filesystem::path(m_impl->config.serverExePath).filename().string();
    return IsProcessRunning(exeName);
#else
    return false;
#endif
}

core::VoidResult SDKEngine::RequestShutdown() {
    if (!IsConnected()) {
        return std::unexpected(core::Error{
            core::ErrorCode::IPCConnectionFailed,
            "Not connected to server",
            "SDKEngine"});
    }

    return m_impl->client->RequestShutdown();
}

void SDKEngine::OnConnectionChanged(std::function<void(ServerConnectionState)> callback) {
    std::lock_guard lock(m_impl->callbackMutex);
    m_impl->onConnectionChanged = std::move(callback);
}

std::string SDKEngine::GetSDKVersion() {
    return "1.0.0";
}

} // namespace openmedia::sdk
