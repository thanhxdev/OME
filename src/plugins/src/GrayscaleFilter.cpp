#include "GrayscaleFilter.h"
#include <cstring>

namespace openmedia::plugins {

bool GrayscaleFilter::ProcessFrame(
    const openmedia::core::MediaFrame& input,
    openmedia::core::MediaFrame& output)
{
    // A simplified example assuming we get NV12 or YUV420P data.
    // In grayscale processing for YUV, we can just copy the Y plane (Luma) 
    // and set the U and V planes (Chroma) to 128 (neutral).
    // Note: In a real implementation we would switch on m_params.pixelFormat.

    // Copy original video info to output
    openmedia::core::VideoFrameInfo info = input.GetVideoInfo();
    
    // Y-plane copy
    int ySize = info.width * info.height;
    if (const uint8_t* inY = input.GetVideoData(0)) {
        if (uint8_t* outY = output.GetVideoData(0)) {
            std::memcpy(outY, inY, ySize);
        }
    }

    // U/V-planes: set to 128 (gray)
    // NV12 has UV interleaved (width * height / 2 bytes)
    // YUV420P has U and V separate (width * height / 4 bytes each)
    int uvSize = ySize / 2; // approximation for 4:2:0
    if (uint8_t* outUV = output.GetVideoData(1)) {
        std::memset(outUV, 128, uvSize);
    }
    
    if (uint8_t* outV = output.GetVideoData(2)) {
        std::memset(outV, 128, uvSize / 2);
    }

    return true;
}

} // namespace openmedia::plugins

// Standard Plugin Export
extern "C" {
#ifdef _WIN32
    __declspec(dllexport)
#else
    __attribute__((visibility("default")))
#endif
    openmedia::plugin::IPlugin* ome_plugin_create() {
        return new openmedia::plugins::GrayscaleFilter();
    }
}
