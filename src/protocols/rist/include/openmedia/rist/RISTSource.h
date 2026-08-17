#pragma once

#include <string>

namespace openmedia {
namespace rist {

class RISTSource {
public:
    RISTSource();
    ~RISTSource();

    bool Connect(const std::string& url);
    void Disconnect();

private:
    bool connected_;
};

} // namespace rist
} // namespace openmedia
