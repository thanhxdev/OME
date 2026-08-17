#include <gtest/gtest.h>
#include <openmedia/ipc/D3D11SharedTexture.h>
#include <openmedia/core/Types.h>

#ifdef _WIN32
#include <d3d11.h>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

class D3D11SharedTextureTest : public ::testing::Test {
protected:
    void SetUp() override {
        // Create a D3D11 device for testing
        UINT creationFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
        
        HRESULT hr = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, creationFlags,
            featureLevels, ARRAYSIZE(featureLevels), D3D11_SDK_VERSION,
            &device, &featureLevel, &context);
            
        if (FAILED(hr)) {
            // Fallback to WARP if hardware is not available
            hr = D3D11CreateDevice(
                nullptr, D3D_DRIVER_TYPE_WARP, nullptr, creationFlags,
                featureLevels, ARRAYSIZE(featureLevels), D3D11_SDK_VERSION,
                &device, &featureLevel, &context);
        }
        
        if (FAILED(hr)) {
            GTEST_SKIP() << "Failed to create D3D11 device for testing";
        }
    }

    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    D3D_FEATURE_LEVEL featureLevel;
};

TEST_F(D3D11SharedTextureTest, InitializeAndDestroy) {
    openmedia::ipc::SharedTexturePoolConfig config;
    config.poolSize = 2;
    config.textureDesc.width = 1920;
    config.textureDesc.height = 1080;
    config.textureDesc.format = openmedia::ipc::SharedTextureFormat::BGRA8;

    openmedia::ipc::D3D11SharedTexturePool pool(device.Get(), config);
    
    auto result = pool.Initialize();
    ASSERT_TRUE(result.has_value());
    EXPECT_TRUE(pool.IsInitialized());
    
    EXPECT_EQ(pool.GetPoolSize(), 2);
    
    auto slots = pool.GetAllSlots();
    ASSERT_EQ(slots.size(), 2);
    
    EXPECT_NE(slots[0].sharedHandle, 0);
    EXPECT_NE(slots[1].sharedHandle, 0);
    
    pool.Destroy();
    EXPECT_FALSE(pool.IsInitialized());
}

TEST_F(D3D11SharedTextureTest, AcquireReleaseCycle) {
    openmedia::ipc::SharedTexturePoolConfig config;
    config.poolSize = 1;
    
    openmedia::ipc::D3D11SharedTexturePool pool(device.Get(), config);
    ASSERT_TRUE(pool.Initialize().has_value());
    
    // Acquire for write
    ID3D11Texture2D* tex = pool.AcquireForWrite(0);
    ASSERT_NE(tex, nullptr);
    
    // Release after write
    pool.ReleaseAfterWrite(0, 12345, 1, true);
    
    auto info = pool.GetSlotInfo(0);
    EXPECT_EQ(info.pts, 12345);
    EXPECT_EQ(info.frameNumber, 1);
    EXPECT_TRUE(info.isKeyFrame);
    EXPECT_TRUE(info.isValid);
    
    // Test OpenShared (Simulate client)
    auto slots = pool.GetAllSlots();
    openmedia::ipc::D3D11SharedTexturePool clientPool(device.Get(), config);
    ASSERT_TRUE(clientPool.OpenShared(slots).has_value());
    
    // Client acquire for read
    ID3D11Texture2D* clientTex = clientPool.AcquireForRead(0);
    ASSERT_NE(clientTex, nullptr);
    
    // Client release
    clientPool.ReleaseAfterRead(0);
}
#else
TEST(D3D11SharedTextureTest, NotSupported) {
    GTEST_SKIP() << "D3D11 shared textures are only supported on Windows";
}
#endif
