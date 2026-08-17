/// @file main.cpp
/// @brief Mixer and Switcher demo with preview.

#include <iostream>
#include <memory>
#include <thread>
#include <chrono>

#include <openmedia/core/Engine.h>
#include <openmedia/core/MediaPipeline.h>
#include <openmedia/core/Logger.h>
#include <openmedia/io/FileSource.h>
#include <openmedia/mixer/Mixer.h>
using namespace openmedia;
using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::mixer;

int main(int argc, char** argv) {
    OME_LOG_INFO(core::Logger::Get("App"), "Starting Mixer Demo...");

    auto engine = Engine::Create();
    auto pipeline = engine->CreatePipeline();

    // Khởi tạo 2 nguồn
    auto sourceA = std::make_shared<FileSource>();
    // sourceA->Open("video_a.mp4");
    
    auto sourceB = std::make_shared<FileSource>();
    // sourceB->Open("video_b.mp4");

    // Khởi tạo Mixer
    auto mixer = std::make_shared<Mixer>();
    mixer->SetOutputFormat(1280, 720, 30);

    pipeline->SetSource(sourceA);
    pipeline->AddFilter(mixer);

    if (pipeline->Build()) {
        pipeline->Start();

        OME_LOG_INFO(core::Logger::Get("App"), "Running Mixer Demo...");

        std::this_thread::sleep_for(std::chrono::seconds(5));

        pipeline->Stop();
    }

    OME_LOG_INFO(core::Logger::Get("App"), "Mixer Demo Exited.");
    return 0;
}
