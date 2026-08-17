#pragma once

#include <string>

namespace openmedia {
namespace st2022 {

class ST2022Output {
public:
    ST2022Output();
    ~ST2022Output();

    bool Start(const std::string& ip, int port);
    void Stop();

private:
    bool started_;
};

} // namespace st2022
} // namespace openmedia
