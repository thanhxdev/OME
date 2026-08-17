#pragma once

#include <string>

namespace openmedia {
namespace st2110 {

class ST2110Source {
public:
    ST2110Source();
    ~ST2110Source();

    bool Connect(const std::string& multicast_ip, int port);
    void Disconnect();

private:
    bool connected_;
};

} // namespace st2110
} // namespace openmedia
