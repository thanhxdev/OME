#pragma once

#include <string>
#include <vector>
#include <memory>
#include <span>
#include <mutex>
#include "openmedia/rist/RISTEngine.h"

namespace openmedia {
namespace rist {

class RISTSource {
public:
    explicit RISTSource(const RISTConfig& config = {});
    ~RISTSource();

    bool Connect(const std::string& url);
    void Disconnect();
    bool IsConnected() const;

    void Configure(const RISTConfig& config);
    RISTConfig GetConfig() const;

    int ReceiveData(std::span<uint8_t> buffer);

    RISTStats GetStats() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace rist
} // namespace openmedia
