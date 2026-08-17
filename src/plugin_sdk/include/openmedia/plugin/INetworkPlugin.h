#pragma once

#include "IPlugin.h"
#include <cstddef>
#include <cstdint>
#include <functional>

namespace openmedia::plugin {

enum class NetworkRole { Source, Output, Bidirectional };

struct NetworkConfig {
    const char* url;
    int port;
    int latency;        // ms
    int timeout;        // ms
    int bufferSize;     // bytes
    const char* encryption;
    const char* passphrase;
};

class INetworkPlugin : public IPlugin {
public:
    virtual bool Connect(const NetworkConfig& config, NetworkRole role) = 0;
    virtual bool Disconnect() = 0;
    virtual bool IsConnected() const = 0;

    // Source mode
    virtual int Receive(uint8_t* buffer, size_t maxSize) = 0;

    // Output mode
    virtual bool Send(const uint8_t* data, size_t size) = 0;

    // Statistics
    virtual const char* GetStatisticsJson() const = 0;

    // Async callback (optional)
    using DataCallback = std::function<void(const uint8_t*, size_t)>;
    virtual void SetDataCallback(DataCallback callback) {}
};

} // namespace openmedia::plugin
