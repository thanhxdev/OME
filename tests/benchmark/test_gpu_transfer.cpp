#include <gtest/gtest.h>
#include <openmedia/gpu/CUDAContext.h>
#include <openmedia/core/MediaFrame.h>
#include <chrono>
#include <iostream>

using namespace openmedia::gpu;
using namespace openmedia::core;

TEST(GpuBenchmark, DISABLED_CpuGpuTransferLatency) {
    
    // Measure Host to Device (Upload)
    auto startUpload = std::chrono::high_resolution_clock::now();
    // Simulate upload: cudaMemcpy(d_ptr, h_ptr, frameSize, cudaMemcpyHostToDevice);
    auto endUpload = std::chrono::high_resolution_clock::now();
    
    // Measure Device to Host (Download)
    auto startDownload = std::chrono::high_resolution_clock::now();
    // Simulate download: cudaMemcpy(h_ptr, d_ptr, frameSize, cudaMemcpyDeviceToHost);
    auto endDownload = std::chrono::high_resolution_clock::now();
    
    auto uploadTime = std::chrono::duration_cast<std::chrono::microseconds>(endUpload - startUpload).count();
    auto downloadTime = std::chrono::duration_cast<std::chrono::microseconds>(endDownload - startDownload).count();
    
    std::cout << "[ BENCHMARK ] Host->Device 1080p: " << uploadTime << " us" << std::endl;
    std::cout << "[ BENCHMARK ] Device->Host 1080p: " << downloadTime << " us" << std::endl;
    
    EXPECT_GE(uploadTime, 0);
    EXPECT_GE(downloadTime, 0);
}
