#pragma once

#include <memory>
#include <deque>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::audio {

/// @brief Buffers audio to allow seeking or shifting backwards in time (replay)
class TimeShift {
public:
    TimeShift();
    ~TimeShift();

    void SetMaxBufferMs(int milliseconds);
    
    void Push(std::shared_ptr<openmedia::core::MediaFrame> input);
    
    /// @brief Retrieve frames from a point in the past
    std::shared_ptr<openmedia::core::MediaFrame> PullShifted(int shiftMs);

private:
    int m_maxBufferMs = 5000;
    std::deque<std::shared_ptr<openmedia::core::MediaFrame>> m_buffer;
};

} // namespace openmedia::audio
