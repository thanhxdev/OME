#pragma once

#include <string>
#include <vector>
#include <memory>
#include <span>
#include <mutex>
#include "openmedia/rist/RISTEngine.h"

namespace openmedia {
namespace rist {

class RISTOutput {
public:
    explicit RISTOutput(const RISTConfig& config = {});
    ~RISTOutput();

    bool Start(const std::string& url);
    void Stop();
    bool IsStarted() const;

    void Configure(const RISTConfig& config);
    RISTConfig GetConfig() const;

    bool AddPeer(const std::string& url, uint32_t weight = 1);
    bool SendData(std::span<const uint8_t> data);

    RISTStats GetStats() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace rist
} // namespace openmedia
