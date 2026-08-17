#pragma once

#include <string>

namespace openmedia {
namespace st2110 {

class NMOSEngine {
public:
    NMOSEngine();
    ~NMOSEngine();

    bool StartRegistration(const std::string& registry_url);
    void StopRegistration();

private:
    bool running_;
};

} // namespace st2110
} // namespace openmedia
