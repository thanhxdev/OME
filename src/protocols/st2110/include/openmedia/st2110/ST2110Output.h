#pragma once

#include <string>

namespace openmedia {
namespace st2110 {

class ST2110Output {
public:
    ST2110Output();
    ~ST2110Output();

    bool Start(const std::string& destination_ip, int port);
    void Stop();

private:
    bool started_;
};

} // namespace st2110
} // namespace openmedia
