#pragma once

#include <string>

namespace openmedia {
namespace st2110 {

class ST2110Engine {
public:
    ST2110Engine();
    ~ST2110Engine();

    bool Initialize();
    void Shutdown();

private:
    bool initialized_;
};

} // namespace st2110
} // namespace openmedia
