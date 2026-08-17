#pragma once

#include <memory>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::audio {

/// @brief Maps audio channels (e.g. Stereo to 5.1, Mono to Stereo)
class ChannelMapper {
public:
    ChannelMapper() = default;
    ~ChannelMapper() = default;

    void SetTargetLayout(openmedia::core::ChannelLayout layout);

    std::shared_ptr<openmedia::core::MediaFrame> Process(std::shared_ptr<openmedia::core::MediaFrame> input);

private:
    openmedia::core::ChannelLayout m_targetLayout = openmedia::core::ChannelLayout::Stereo;
};

} // namespace openmedia::audio
