#pragma once

#include <openmedia/plugin/IVideoFilter.h>
#include <openmedia/ipc/IPCClient.h>
#include <memory>
#include <string>

namespace openmedia::plugin {

class SandboxedPlugin : public IVideoFilter {
public:
    SandboxedPlugin(const std::string& pluginPath, const std::string& pipeName);
    ~SandboxedPlugin() override;

    // IPlugin
    bool Initialize() override;
    void Shutdown() override;
    const PluginInfo& GetInfo() const override;
    bool Configure(const char* jsonConfig) override;
    const char* GetDefaultConfig() const override;

    // IVideoFilter
    bool Setup(const VideoFilterParams& params) override;
    bool ProcessFrame(const core::MediaFrame& input, core::MediaFrame& output) override;
    bool ProcessFrameGPU(const void* inputTexture, void* outputTexture, int textureFormat) override;
    VideoFilterParams GetOutputParams() const override;
    void Reset() override;

private:
    void RestartSandbox();

    std::string m_pluginPath;
    std::string m_pipeName;
    ipc::IPCClientConfig m_clientConfig;
    std::unique_ptr<ipc::IPCClient> m_client;

    PluginInfo m_info;
    VideoFilterParams m_params;
};

} // namespace openmedia::plugin
