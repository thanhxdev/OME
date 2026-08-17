#include <openmedia/io/FileSource.h>
#include <openmedia/io/DesktopCapture.h>
#include <openmedia/io/WindowCapture.h>
#include <openmedia/rtmp/RTMPOutput.h>
#include <openmedia/core/Logger.h>
#include <iostream>
#include <thread>
#include <chrono>

using namespace openmedia;

void PrintUsage() {
    std::cout << "Usage: rtmp_demo [mode] [options]\n";
    std::cout << "Modes:\n";
    std::cout << "  --desktop [index]     Capture desktop (default index 0)\n";
    std::cout << "  --window [title]      Capture a specific window\n";
    std::cout << "  --file [path]         Capture a media file (default: sample.mp4)\n";
    std::cout << "Options:\n";
    std::cout << "  --region x y w h      Capture specific region (only for --desktop)\n";
}

int main(int argc, char* argv[]) {
    openmedia::core::Logger::SInfo("OME", "Starting RTMP Demo (Screen Recorder/Streamer)");

    std::string mode = "--file";
    std::string file_path = "sample.mp4";
    int displayIndex = 0;
    std::string windowTitle = "";
    bool hasRegion = false;
    int rx = 0, ry = 0, rw = 0, rh = 0;

    for (int i = 1; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg == "--desktop") {
            mode = arg;
            if (i + 1 < argc && argv[i+1][0] != '-') {
                displayIndex = std::stoi(argv[++i]);
            }
        } else if (arg == "--window") {
            mode = arg;
            if (i + 1 < argc) windowTitle = argv[++i];
        } else if (arg == "--file") {
            mode = arg;
            if (i + 1 < argc) file_path = argv[++i];
        } else if (arg == "--region") {
            if (i + 4 < argc) {
                rx = std::stoi(argv[++i]);
                ry = std::stoi(argv[++i]);
                rw = std::stoi(argv[++i]);
                rh = std::stoi(argv[++i]);
                hasRegion = true;
            }
        } else {
            PrintUsage();
            return 1;
        }
    }

    std::shared_ptr<core::IMediaObject> source;
    
    if (mode == "--desktop") {
        auto desktopSource = std::make_shared<io::DesktopCapture>();
        desktopSource->SetDisplayIndex(displayIndex);
        if (hasRegion) {
            desktopSource->SetCaptureRegion(rx, ry, rw, rh);
        }
        desktopSource->Initialize();
        source = desktopSource;
    } else if (mode == "--window") {
        auto windowSource = std::make_shared<io::WindowCapture>();
        windowSource->SetTargetWindowTitle(windowTitle);
        windowSource->Initialize();
        source = windowSource;
    } else {
        auto fileSource = std::make_shared<io::FileSource>();
        auto resOpen = fileSource->Open(file_path);
        if (!resOpen.has_value()) {
            openmedia::core::Logger::SError("OME", "Failed to open input file.");
            return 1;
        }
        source = fileSource;
    }

    rtmp::RTMPOutput output;
    output.AddVideoStream(1280, 720, 30, "libx264"); // Add dummy stream to initialize context
    auto resOut = output.Open("rtmp://localhost/live/test");
    if (!resOut.has_value()) {
        openmedia::core::Logger::SError("OME", "Failed to open RTMP output (Is there a local RTMP server running?). Will run capture without pushing.");
    }

    source->Start();
    output.Start();

    openmedia::core::Logger::SInfo("OME", "Recording/Streaming started. Running for 100 frames...");

    for (int i = 0; i < 100; ++i) {
        auto frame = source->PullFrame();
        if (frame.has_value()) {
            if (resOut.has_value()) output.PushFrame(frame.value());
            openmedia::core::Logger::SInfo("OME", "Pushed frame {}", i);
        } else {
            openmedia::core::Logger::SInfo("OME", "Waiting for frame...");
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(33)); // ~30fps
    }

    output.Stop();
    source->Stop();
    
    openmedia::core::Logger::SInfo("OME", "RTMP Demo finished.");
    return 0;
}
