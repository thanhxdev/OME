#pragma once

#include <string>

namespace openmedia::ndi {

class NDISource {
public:
    NDISource();
    ~NDISource();

    bool Connect(const std::string& sourceName);
    void Disconnect();

    void EnableHX(bool enable);
    std::string FetchMetadata() const;

private:
    void* m_ndiReceiver = nullptr;
    bool m_hxEnabled = false;
};

} // namespace openmedia::ndi
