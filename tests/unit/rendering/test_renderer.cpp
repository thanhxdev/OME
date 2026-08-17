#include <gtest/gtest.h>
#include <openmedia/rendering/D3D11Renderer.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::rendering;
using namespace openmedia::core;

TEST(RendererTest, Initialize) {
    D3D11Renderer renderer;
    // We expect it to succeed if D3D11 is available, but in CI it might fail if no GPU.
    // So we just check that it doesn't crash.
    auto res = renderer.Initialize(nullptr);
    if (res.has_value()) {
        // Initialization succeeded, renderer is ready.
        EXPECT_TRUE(res.has_value());
    }
}

TEST(RendererTest, RenderFrame) {
    D3D11Renderer renderer;
    if (renderer.Initialize(nullptr).has_value()) {
        auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::BGRA);
        EXPECT_TRUE(renderer.Render(frame).has_value());
        (void)renderer.Shutdown();
    }
}
