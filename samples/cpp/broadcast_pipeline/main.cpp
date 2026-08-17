/// @file main.cpp
/// @brief Multi-source, multi-output broadcast pipeline sample.

#include <iostream>
#include <memory>
#include <thread>
#include <chrono>

#include <openmedia/core/Engine.h>
#include <openmedia/core/MediaPipeline.h>
#include <openmedia/core/Logger.h>
#include <openmedia/io/FileSource.h>
#include <openmedia/io/FileOutput.h>
#include <openmedia/mixer/Mixer.h>
#include <openmedia/overlay/OverlayEngine.h>
#include <openmedia/overlay/LogoOverlay.h>
#include <openmedia/overlay/TextOverlay.h>
#include <openmedia/codecs/FFmpegH264Encoder.h>

using namespace openmedia;
using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::mixer;
using namespace openmedia::overlay;
using namespace openmedia::codecs;

int main(int argc, char** argv) {
    OME_LOG_INFO(core::Logger::Get("App"), "Starting Broadcast Pipeline Sample...");

    // 1. Khởi tạo Engine
    auto engine = Engine::Create();
    if (!engine) {
        OME_LOG_ERROR(core::Logger::Get("App"), "Failed to create OpenMedia Engine");
        return -1;
    }

    // 2. Tạo Pipeline
    auto pipeline = engine->CreatePipeline();

    // 3. Khởi tạo Đầu vào (Inputs)
    // Ví dụ: file video nền (A) và camera trực tiếp (B)
    auto sourceA = std::make_shared<FileSource>();
    // sourceA->Open("background.mp4");
    
    // Giả lập source B là Live Stream hoặc Device Capture
    auto sourceB = std::make_shared<FileSource>(); 
    // sourceB->Open("camera_feed.mp4");

    // 4. Khởi tạo Mixer (Bộ trộn video)
    auto mixer = std::make_shared<Mixer>();
    mixer->SetOutputFormat(1920, 1080, 60.0);
    
    // Gắn sourceA vào Layer 0, sourceB vào Layer 1
    // (Trong pipeline thực tế, Mixer sẽ lắng nghe từ sourceA/sourceB. 
    // Với Node-based pipeline, ta có thể link: pipeline->Connect(sourceA, mixer, layerA))
    
    // 5. Khởi tạo Overlays (Đồ họa hiển thị trên cùng)
    auto overlayEngine = std::make_shared<OverlayEngine>();
    
    auto logo = std::make_shared<LogoOverlay>("logo1", "logo.png");
    // logo->SetPosition(1700, 50); // Góc phải trên
    
    auto text = std::make_shared<TextOverlay>("text1", "LIVE - OpenMedia Broadcast");
    // text->SetPosition(50, 50); // Góc trái trên
    
    overlayEngine->AddOverlay(logo);
    overlayEngine->AddOverlay(text);

    // 6. Khởi tạo Encoders và Outputs (Đầu ra đa điểm)
    auto encoder = std::make_shared<FFmpegH264Encoder>(); // Hoặc nvenc, qsv
    
    // Ghi ra file MP4 lưu trữ
    auto archiveOut = std::make_shared<FileOutput>();
    // archiveOut->Open("archive_output.mp4");
    
    // (Mở rộng cho SRT/RTMP)
    // auto srtOut = std::make_shared<SRTOutput>();
    // srtOut->Open("srt://127.0.0.1:9000");

    // 7. Xây dựng đồ thị Pipeline (Node connection)
    // Thiết lập tuần tự (Linear Pipeline Builder)
    pipeline->SetSource(sourceA); // Main source
    pipeline->AddFilter(mixer);
    pipeline->AddFilter(overlayEngine);
    pipeline->SetEncoder(encoder);
    pipeline->AddOutput(archiveOut);
    // pipeline->AddOutput(srtOut); // Multi-output
    
    // 8. Chạy pipeline
    if (auto buildRes = pipeline->Build(); buildRes.has_value()) {
        OME_LOG_INFO(core::Logger::Get("App"), "Pipeline built successfully. Starting...");
        pipeline->Start();

        // Chạy trong 10 giây (giả lập livestream)
        std::this_thread::sleep_for(std::chrono::seconds(10));
        
        OME_LOG_INFO(core::Logger::Get("App"), "Stopping pipeline...");
        pipeline->Stop();
    } else {
        OME_LOG_ERROR(core::Logger::Get("App"), "Failed to build pipeline.");
    }

    OME_LOG_INFO(core::Logger::Get("App"), "Broadcast Pipeline Sample Exited.");
    return 0;
}
