#pragma once

#include <string>

namespace openmedia::ndi {

class NDIOutput {
public:
    NDIOutput();
    ~NDIOutput();

    bool Start(const std::string& sourceName);
    void Stop();

private:
    void* m_ndiSender = nullptr;
};

} // namespace openmedia::ndi
