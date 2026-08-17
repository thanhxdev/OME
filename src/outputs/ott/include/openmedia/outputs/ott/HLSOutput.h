#pragma once

#include <string>

namespace openmedia {
namespace outputs {
namespace ott {

class HLSOutput {
public:
    HLSOutput();
    ~HLSOutput();

    bool Start(const std::string& output_dir, const std::string& manifest_name);
    void Stop();

private:
    bool started_;
};

} // namespace ott
} // namespace outputs
} // namespace openmedia
