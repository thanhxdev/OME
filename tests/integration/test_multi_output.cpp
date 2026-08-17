#include <gtest/gtest.h>
#include <openmedia/io/FileSource.h>
#include <openmedia/io/FileOutput.h>
#include <openmedia/srt/SRTOutput.h>
#include <openmedia/rtmp/RTMPOutput.h>
#include <openmedia/mixer/Mixer.h>
#include <memory>
#include <chrono>
#include <thread>

using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::srt;
using namespace openmedia::rtmp;
using namespace openmedia::mixer;

TEST(IntegrationTest, MultiOutputPipeline) {
    // 1. Source
    auto source = std::make_shared<FileSource>();
    
    // 2. Mixer (acts as dispatcher for multiple sinks if extended, but here we can mock by fanning out)
    auto mixer = std::make_shared<Mixer>();
    mixer->SetOutputFormat(1920, 1080, 60);
    
    // We mock fan-out by creating three output destinations
    auto fileOutput = std::make_shared<FileOutput>();
    auto srtOutput = std::make_shared<SRTOutput>();
    auto rtmpOutput = std::make_shared<RTMPOutput>();

    // Start all
    source->Start();
    mixer->Start();
    fileOutput->Start();
    srtOutput->Start("srt://127.0.0.1:9000");
    rtmpOutput->Start();
    
    // Connect them (mock) - normally you'd use a router/splitter
    // Here we just test they can all be instantiated and started in the same process
    
    // Process frames
    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    
    // Stop all
    rtmpOutput->Stop();
    srtOutput->Stop();
    fileOutput->Stop();
    mixer->Stop();
    source->Stop();
    
    SUCCEED();
}
