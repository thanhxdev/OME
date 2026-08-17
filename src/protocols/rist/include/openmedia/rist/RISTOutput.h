#pragma once

#include <string>

namespace openmedia {
namespace rist {

class RISTOutput {
public:
    RISTOutput();
    ~RISTOutput();

    bool Start(const std::string& url);
    void Stop();

private:
    bool started_;
};

} // namespace rist
} // namespace openmedia
