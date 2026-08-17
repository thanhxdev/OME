#pragma once

#include <string>

namespace openmedia {
namespace rist {

class RISTEngine {
public:
    RISTEngine();
    ~RISTEngine();

    bool Initialize();
    void Shutdown();

private:
    bool initialized_;
};

} // namespace rist
} // namespace openmedia
