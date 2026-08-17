#pragma once
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/ErrorCodes.h>
#include <memory>

namespace openmedia::mixer {

class CropFilter {
public:
    CropFilter();
    ~CropFilter();

    void SetCrop(int x, int y, int width, int height);

    core::Result<std::shared_ptr<core::MediaFrame>> Process(const std::shared_ptr<core::MediaFrame>& input);

private:
    int m_x;
    int m_y;
    int m_width;
    int m_height;
};

} // namespace openmedia::mixer
