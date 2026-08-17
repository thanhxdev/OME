#include "openmedia/ndi/NDIOutput.h"
#include <Processing.NDI.Lib.h>
#include <spdlog/spdlog.h>

namespace openmedia::ndi {

NDIOutput::NDIOutput() = default;

NDIOutput::~NDIOutput() {
    Stop();
}

bool NDIOutput::Start(const std::string& sourceName) {
    if (m_ndiSender) return true;

    NDIlib_send_create_t send_create_desc;
    send_create_desc.p_ndi_name = sourceName.c_str();
    send_create_desc.p_groups = nullptr;
    send_create_desc.clock_video = true;
    send_create_desc.clock_audio = false;

    m_ndiSender = NDIlib_send_create(&send_create_desc);
    if (!m_ndiSender) {
        spdlog::error("Failed to create NDI sender: {}", sourceName);
        return false;
    }

    spdlog::info("NDI sender started: {}", sourceName);
    return true;
}

void NDIOutput::Stop() {
    if (!m_ndiSender) return;

    NDIlib_send_destroy(static_cast<NDIlib_send_instance_t>(m_ndiSender));
    m_ndiSender = nullptr;
    spdlog::info("NDI sender stopped");
}

} // namespace openmedia::ndi
