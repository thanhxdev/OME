#include <gtest/gtest.h>
#include <openmedia/gpu/D3D11Context.h>
#include <openmedia/gpu/GPUFrame.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::core;
using namespace openmedia::gpu;

TEST(D3D11ContextTest, InitializationAndShutdown) {
    auto context = std::make_shared<D3D11Context>();
    auto result = context->Initialize();
    
    // Some CI environments might not have a GPU, so we only proceed if initialized
    if (!result.has_value()) {
        GTEST_SKIP() << "Failed to initialize D3D11, skipping test (might be running in headless CI without WARP)";
    }

    EXPECT_NE(context->GetDeviceHandle(), nullptr);
    EXPECT_EQ(context->GetType(), GPUType::DirectX11);
    
    context->Shutdown();
    EXPECT_EQ(context->GetDeviceHandle(), nullptr);
}

TEST(D3D11ContextTest, TextureUploadDownload) {
    auto context = std::make_shared<D3D11Context>();
    auto resInit = context->Initialize();
    
    if (!resInit.has_value()) {
        GTEST_SKIP() << "Failed to initialize D3D11, skipping test";
    }

    const int width = 128;
    const int height = 128;
    
    // Create CPU dummy data (green frame)
    std::vector<uint8_t> inData(width * height * 4);
    for (int y = 0; y < height; ++y) {
        for (int x = 0; x < width; ++x) {
            inData[(y * width + x) * 4 + 0] = 0;   // B
            inData[(y * width + x) * 4 + 1] = 255; // G
            inData[(y * width + x) * 4 + 2] = 0;   // R
            inData[(y * width + x) * 4 + 3] = 255; // A
        }
    }

    // Upload
    void* texHandle = nullptr;
    auto resUpload = context->UploadTexture(inData.data(), width, height, PixelFormat::BGRA, &texHandle);
    ASSERT_TRUE(resUpload.has_value()) << "Texture upload failed";
    ASSERT_NE(texHandle, nullptr);

    // Create GPUFrame to manage it
    auto gpuFrame = GPUFrame::Create(context, texHandle, width, height, PixelFormat::BGRA);
    ASSERT_NE(gpuFrame, nullptr);
    EXPECT_EQ(gpuFrame->GetGPUTextureHandle(), texHandle);

    // Download
    std::vector<uint8_t> outData(width * height * 4, 0); // initialize with 0
    auto resDownload = context->DownloadTexture(texHandle, outData.data(), width, height, PixelFormat::BGRA);
    ASSERT_TRUE(resDownload.has_value()) << "Texture download failed";

    // Verify
    bool match = true;
    for (size_t i = 0; i < inData.size(); ++i) {
        if (inData[i] != outData[i]) {
            match = false;
            break;
        }
    }
    EXPECT_TRUE(match) << "Downloaded texture data does not match uploaded data";

    // Cleanup happens automatically when gpuFrame goes out of scope
}

TEST(D3D11ContextTest, TextureCopy) {
    auto context = std::make_shared<D3D11Context>();
    if (!context->Initialize().has_value()) {
        GTEST_SKIP() << "Failed to initialize D3D11, skipping test";
    }

    const int width = 64;
    const int height = 64;
    std::vector<uint8_t> inData(width * height * 4, 128); // Gray

    void* texHandle1 = nullptr;
    ASSERT_TRUE(context->UploadTexture(inData.data(), width, height, PixelFormat::BGRA, &texHandle1).has_value());
    auto gpuFrame1 = GPUFrame::Create(context, texHandle1, width, height, PixelFormat::BGRA);

    void* texHandle2 = nullptr;
    std::vector<uint8_t> dummyData(width * height * 4, 0);
    ASSERT_TRUE(context->UploadTexture(dummyData.data(), width, height, PixelFormat::BGRA, &texHandle2).has_value());
    auto gpuFrame2 = GPUFrame::Create(context, texHandle2, width, height, PixelFormat::BGRA);

    auto resCopy = context->CopyTexture(texHandle1, texHandle2);
    ASSERT_TRUE(resCopy.has_value());

    std::vector<uint8_t> outData(width * height * 4, 0);
    ASSERT_TRUE(context->DownloadTexture(texHandle2, outData.data(), width, height, PixelFormat::BGRA).has_value());

    EXPECT_EQ(inData, outData);
}

TEST(D3D11ContextTest, ShaderMixing) {
    auto context = std::make_shared<D3D11Context>();
    if (!context->Initialize().has_value()) {
        GTEST_SKIP() << "Failed to initialize D3D11, skipping test";
    }

    // Try to init pipeline (assuming shaders are in 'shaders/' directory)
    auto resInit = context->InitMixerPipeline("shaders/MixerVertexShader.hlsl", "shaders/MixerPixelShader.hlsl");
    if (!resInit.has_value()) {
        GTEST_SKIP() << "Failed to init mixer pipeline (shaders missing?), skipping test";
    }

    const int width = 32;
    const int height = 32;
    
    // BG: Red (BGRA = 0, 0, 255, 255)
    std::vector<uint8_t> bgData(width * height * 4);
    for (size_t i = 0; i < bgData.size(); i += 4) {
        bgData[i+0] = 0;   // B
        bgData[i+1] = 0;   // G
        bgData[i+2] = 255; // R
        bgData[i+3] = 255; // A
    }

    // Layer: Blue (BGRA = 255, 0, 0, 255)
    std::vector<uint8_t> layerData(width * height * 4);
    for (size_t i = 0; i < layerData.size(); i += 4) {
        layerData[i+0] = 255; // B
        layerData[i+1] = 0;   // G
        layerData[i+2] = 0;   // R
        layerData[i+3] = 255; // A
    }

    void* bgTex = nullptr;
    void* layerTex = nullptr;
    void* outTex = nullptr;
    
    ASSERT_TRUE(context->UploadTexture(bgData.data(), width, height, PixelFormat::BGRA, &bgTex).has_value());
    ASSERT_TRUE(context->UploadTexture(layerData.data(), width, height, PixelFormat::BGRA, &layerTex).has_value());
    
    // Create an empty texture for output
    std::vector<uint8_t> outInit(width * height * 4, 0);
    ASSERT_TRUE(context->UploadTexture(outInit.data(), width, height, PixelFormat::BGRA, &outTex).has_value());

    // Mix with 0.5 opacity
    auto resMix = context->ExecuteMixerPipeline(bgTex, layerTex, outTex, 0.5f);
    if (!resMix.has_value()) {
        FAIL() << "Mixer pipeline execution failed: " << resMix.error().message;
    }

    // Download and check
    std::vector<uint8_t> outData(width * height * 4, 0);
    ASSERT_TRUE(context->DownloadTexture(outTex, outData.data(), width, height, PixelFormat::BGRA).has_value());

    // Check middle pixel
    int idx = ((height/2) * width + (width/2)) * 4;
    // Expected: Blue*0.5 + Red*0.5 -> B=127, G=0, R=127, A=255
    // Small tolerance due to GPU float math
    EXPECT_NEAR(outData[idx+0], 127, 2); // B
    EXPECT_NEAR(outData[idx+1], 0, 2);   // G
    EXPECT_NEAR(outData[idx+2], 127, 2); // R
    EXPECT_NEAR(outData[idx+3], 255, 2); // A

    context->FreeTexture(bgTex);
    context->FreeTexture(layerTex);
    context->FreeTexture(outTex);
}
