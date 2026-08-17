#pragma once

#include <string>

namespace openmedia {
namespace st2022 {

class ST2022Engine {
public:
    ST2022Engine();
    ~ST2022Engine();

    bool Initialize();
    void Shutdown();

private:
    bool initialized_;
};

} // namespace st2022
} // namespace openmedia
