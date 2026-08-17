#include <openmedia/ipc/D3D11SharedTexture.h>
#include <openmedia/ipc/IPCServer.h>
#include <openmedia/ipc/IPCClient.h>
#include <openmedia/ipc/CommandTypes.h>

#include <iostream>
#include <string>
#include <thread>
#include <chrono>
#include <vector>

#ifdef _WIN32
#include <d3d11.h>
#include <wrl/client.h>
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

using Microsoft::WRL::ComPtr;

// Helper to create D3D11 Device
bool CreateDevice(ComPtr<ID3D11Device>& device, ComPtr<ID3D11DeviceContext>& context) {
    UINT creationFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if defined(_DEBUG)
    // creationFlags |= D3D11_CREATE_DEVICE_DEBUG; // May fail if graphics tools not installed
#endif
    D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL featureLevel;
    
    HRESULT hr = D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, creationFlags,
        featureLevels, ARRAYSIZE(featureLevels), D3D11_SDK_VERSION,
        &device, &featureLevel, &context);
        
    if (FAILED(hr)) {
        hr = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_WARP, nullptr, creationFlags,
            featureLevels, ARRAYSIZE(featureLevels), D3D11_SDK_VERSION,
            &device, &featureLevel, &context);
    }
    
    return SUCCEEDED(hr);
}

void RunServer(const std::string& pipeName) {
    std::cout << "[SERVER] Starting Server Mode...\n";
    
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    if (!CreateDevice(device, context)) {
        std::cerr << "[SERVER] Failed to create D3D11 Device.\n";
        return;
    }
    
    // Create pool
    openmedia::ipc::SharedTexturePoolConfig config;
    config.poolSize = 2; // Double buffering
    config.textureDesc.width = 1920;
    config.textureDesc.height = 1080;
    config.textureDesc.format = openmedia::ipc::SharedTextureFormat::BGRA8;
    
    openmedia::ipc::D3D11SharedTexturePool pool(device.Get(), config);
    if (!pool.Initialize()) {
        std::cerr << "[SERVER] Failed to initialize D3D11SharedTexturePool.\n";
        return;
    }
    
    std::cout << "[SERVER] Created shared textures successfully.\n";
    auto slots = pool.GetAllSlots();
    
    // Setup IPC
    openmedia::ipc::IPCServerConfig ipcConfig;
    ipcConfig.pipeConfig.pipeName = pipeName;
    openmedia::ipc::IPCServer ipcServer(ipcConfig);
    
    // When client asks for texture share, we send the slots
    ipcServer.RegisterHandler(openmedia::ipc::CommandType::ShareD3D11Texture, 
        [&slots](uint32_t clientId, const std::vector<uint8_t>& payload) {
            std::cout << "[SERVER] Received ShareD3D11Texture request from client " << clientId << ".\n";
            std::vector<uint8_t> response;
            const uint8_t* bytePtr = reinterpret_cast<const uint8_t*>(slots.data());
            response.assign(bytePtr, bytePtr + (slots.size() * sizeof(openmedia::ipc::SharedTextureSlot)));
            return response;
        }
    );
    
    if (!ipcServer.Start()) {
        std::cerr << "[SERVER] Failed to start IPC Server on pipe: " << pipeName << "\n";
        return;
    }
    
    std::cout << "[SERVER] Waiting for client to connect...\n";
    
    // Simulated render loop (just write data)
    uint64_t pts = 0;
    uint32_t slotIndex = 0;
    for (int i = 0; i < 100; ++i) { // simulate 100 frames
        ID3D11Texture2D* tex = pool.AcquireForWrite(slotIndex);
        if (tex) {
            // Write green color to texture as simulation (in real world: GPU shader / Video Decoder output)
            std::cout << "[SERVER] Rendered frame " << i << " to slot " << slotIndex << "\n";
            pool.ReleaseAfterWrite(slotIndex, pts, i, true);
            slotIndex = (slotIndex + 1) % config.poolSize;
            pts += 33333; // ~30fps
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(33)); // 30fps
    }
    
    std::cout << "[SERVER] Shutting down...\n";
    ipcServer.Stop();
}

void RunClient(const std::string& pipeName) {
    std::cout << "[CLIENT] Starting Client Mode...\n";
    
    // Setup IPC
    openmedia::ipc::IPCClientConfig ipcConfig;
    ipcConfig.pipeConfig.pipeName = pipeName;
    ipcConfig.autoLaunchServer = false;
    
    openmedia::ipc::IPCClient ipcClient(ipcConfig);
    
    std::cout << "[CLIENT] Connecting to server...\n";
    // Keep trying to connect
    while (!ipcClient.Connect()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(500));
        std::cout << "[CLIENT] Retrying connection...\n";
    }
    
    std::cout << "[CLIENT] Connected! Requesting shared textures...\n";
    auto result = ipcClient.SendCommand(openmedia::ipc::CommandType::ShareD3D11Texture);
    
    if (!result) {
        std::cerr << "[CLIENT] Failed to get texture slots from server.\n";
        return;
    }
    
    // Parse slots
    const std::vector<uint8_t>& payload = result.value();
    size_t numSlots = payload.size() / sizeof(openmedia::ipc::SharedTextureSlot);
    std::vector<openmedia::ipc::SharedTextureSlot> slots(numSlots);
    std::memcpy(slots.data(), payload.data(), payload.size());
    
    std::cout << "[CLIENT] Received " << numSlots << " shared texture slots.\n";
    
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    if (!CreateDevice(device, context)) {
        std::cerr << "[CLIENT] Failed to create D3D11 Device.\n";
        return;
    }
    
    openmedia::ipc::D3D11SharedTexturePool clientPool(device.Get());
    if (!clientPool.OpenShared(slots)) {
        std::cerr << "[CLIENT] Failed to map shared textures.\n";
        return;
    }
    
    std::cout << "[CLIENT] Successfully mapped shared textures!\n";
    
    // Simulated read loop
    uint32_t slotIndex = 0;
    for (int i = 0; i < 50; ++i) {
        ID3D11Texture2D* tex = clientPool.AcquireForRead(slotIndex);
        if (tex) {
            auto info = clientPool.GetSlotInfo(slotIndex);
            std::cout << "[CLIENT] Read frame " << info.frameNumber << " (PTS: " << info.pts << ") from slot " << slotIndex << "\n";
            
            // In real app: draw texture to screen or process it
            clientPool.ReleaseAfterRead(slotIndex);
            slotIndex = (slotIndex + 1) % numSlots;
        } else {
            std::cout << "[CLIENT] Waiting for frame on slot " << slotIndex << "...\n";
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
        }
    }
    
    std::cout << "[CLIENT] Shutting down...\n";
    ipcClient.Disconnect();
}

int main(int argc, char** argv) {
    std::string mode = "server";
    if (argc > 1) {
        mode = argv[1];
    }
    
    std::string pipeName = "\\\\.\\pipe\\OpenMedia_D3D11_PoC";
    
    if (mode == "--client") {
        RunClient(pipeName);
    } else {
        RunServer(pipeName);
    }
    
    return 0;
}
#else
int main() {
    std::cout << "D3D11 is only supported on Windows.\n";
    return 0;
}
#endif
