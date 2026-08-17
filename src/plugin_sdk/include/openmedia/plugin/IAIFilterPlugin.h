#pragma once

#include "IVideoFilter.h"

namespace openmedia::plugin {

enum class AIFilterType {
    Denoise,
    Upscale,
    Segmentation,
    BackgroundRemoval,
    FaceDetection,
    ObjectDetection,
    StyleTransfer,
    FrameInterpolation,
    Custom
};

class IAIFilterPlugin : public IVideoFilter {
public:
    virtual AIFilterType GetAIFilterType() const = 0;

    // Model management
    virtual bool LoadModel(const char* modelPath) = 0;
    virtual bool IsModelLoaded() const = 0;

    // Inference device
    virtual bool SetInferenceDevice(const char* device) = 0; // "cpu", "cuda:0", etc.

    // Confidence threshold (for detection)
    virtual void SetConfidence(float threshold) {}
};

} // namespace openmedia::plugin
