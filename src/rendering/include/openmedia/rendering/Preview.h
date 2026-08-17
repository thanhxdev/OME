#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/rendering/Renderer.h>
#include <memory>

namespace openmedia::rendering {

class Preview {
public:
    Preview(void* hwnd);
    ~Preview();

    core::Result<void> Initialize();
    core::Result<void> DisplayFrame(const std::shared_ptr<core::MediaFrame>& frame);
    void OnResize(int width, int height);
    void SetScaleMode(ScaleMode mode);

private:
    void* m_hwnd;
    std::unique_ptr<Renderer> m_renderer;
};

} // namespace openmedia::rendering
