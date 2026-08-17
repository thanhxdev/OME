#pragma once

#include <string>

namespace openmedia {
namespace st2022 {

class ST2022Source {
public:
    ST2022Source();
    ~ST2022Source();

    bool Connect(const std::string& ip, int port);
    void Disconnect();

private:
    bool connected_;
};

} // namespace st2022
} // namespace openmedia
