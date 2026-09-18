#pragma once

#include <string>
#include <memory>
#include <mutex>
#include <openmedia/core/MediaFrame.h>

namespace openmedia {
namespace outputs {
namespace decklink {

class DeckLinkOutput {
public:
    DeckLinkOutput();
    ~DeckLinkOutput();

    bool Start(int device_index, int video_mode);
    void Stop();
    bool IsStarted() const;

    /// @brief Push MediaFrame to SDI/HDMI hardware output
    bool DisplayVideoFrame(std::shared_ptr<core::MediaFrame> frame);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace decklink
} // namespace outputs
} // namespace openmedia
