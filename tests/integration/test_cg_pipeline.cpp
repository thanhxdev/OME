#include <iostream>
#include <memory>
#include <thread>
#include <chrono>
#include <fstream>
#include <filesystem>
#include <openmedia/cg/CGEngine.h>
#include <openmedia/mixer/Mixer.h>
#include <openmedia/mixer/MixerLayer.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::core;
using namespace openmedia::cg;
using namespace openmedia::mixer;

// Helper to save BGRA frame to a raw file
void SaveFrameToRaw(std::shared_ptr<MediaFrame> frame, const std::string& filename) {
    if (!frame || frame->GetTotalSize() == 0) return;
    std::ofstream file(filename, std::ios::binary);
    file.write(reinterpret_cast<const char*>(frame->GetVideoPlane(0)), frame->GetTotalSize());
    std::cout << "Saved snapshot to " << filename << std::endl;
}

int main(int argc, char* argv[]) {
    // 1. Initialize CEF subprocess (crucial for multi-process architecture)
    int exit_code = CGEngine::InitializeSubProcess();
    if (exit_code >= 0) {
        return exit_code; // If it's a subprocess, it returns here.
    }

    std::cout << "--- Starting CG Pipeline Test ---" << std::endl;

    // 1. Determine HTML template path
    std::filesystem::path currentPath = std::filesystem::current_path();
    std::string templatePath = "file:///" + currentPath.string() + "/tests/data/cg_template.html";
    // Replace backslashes with forward slashes for URI
    for (char& c : templatePath) { if (c == '\\') c = '/'; }

    // 2. Initialize CGEngine
    auto cgEngine = std::make_shared<CGEngine>(1920, 1080);
    cgEngine->LoadTemplate(templatePath);

    // --- Render and mix frames ---
    auto mixer = std::make_shared<Mixer>();
    mixer->SetOutputFormat(1920, 1080, 60);
    mixer->Start(); // Start mixer

    // Allow CEF some time to load the HTML page and render the first frame
    std::this_thread::sleep_for(std::chrono::seconds(2));

    // Bind some dynamic data
    cgEngine->BindData("title", "LIVE: BẢN TIN THỜI SỰ");
    cgEngine->BindData("subtitle", "Kiểm thử CGEngine với HTML Overlay");

    // Give CEF time to render the changes
    for (int i = 0; i < 10; ++i) {
        cgEngine->DoMessageLoopWork();
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }

    // 3. Render the CG frame
    auto cgFrame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::BGRA);
    cgEngine->Render(cgFrame);

    // Save the raw CG frame
    SaveFrameToRaw(cgFrame, "cg_output.raw");

    // 4. Setup Mixer to overlay CG on a green background
    // Layer 0: Background (Green)
    auto bgLayer = std::make_shared<MixerLayer>(0, "Background");
    auto bgFrame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::BGRA);
    uint8_t* bgData = bgFrame->GetVideoPlane(0);
    for (size_t i = 0; i < bgFrame->GetTotalSize(); i += 4) {
        bgData[i]   = 0;   // B
        bgData[i+1] = 255; // G
        bgData[i+2] = 0;   // R
        bgData[i+3] = 255; // A
    }
    bgLayer->PushFrame(bgFrame);
    mixer->AddLayer(bgLayer);

    // Layer 1: CG Overlay
    auto cgLayer = std::make_shared<MixerLayer>(1, "CG Overlay");
    cgLayer->SetZIndex(1); // On top
    cgLayer->PushFrame(cgFrame);
    mixer->AddLayer(cgLayer);

    // Create a downstream sink to receive the mixed frame
    class FrameSink : public openmedia::core::IMediaObject {
    public:
        std::shared_ptr<openmedia::core::MediaFrame> outputFrame;
        bool frameReceived = false;
        
        openmedia::core::VoidResult PushFrame(std::shared_ptr<openmedia::core::MediaFrame> frame) override {
            if (!frameReceived) {
                outputFrame = frame;
                frameReceived = true;
            }
            return {};
        }

        std::string GetName() const override { return "Sink"; }
        openmedia::core::PipelineState GetState() const override { return openmedia::core::PipelineState::Running; }
        openmedia::core::VoidResult Initialize() override { return {}; }
        openmedia::core::VoidResult Start() override { return {}; }
        openmedia::core::VoidResult Stop() override { return {}; }
        openmedia::core::Result<std::shared_ptr<openmedia::core::MediaFrame>> PullFrame() override { return std::unexpected(openmedia::core::Error::Make(openmedia::core::ErrorCode::NotImplemented, "Not implemented")); }
        openmedia::core::VoidResult Connect(std::shared_ptr<openmedia::core::IMediaObject>) override { return {}; }
        openmedia::core::VoidResult Disconnect() override { return {}; }
        void OnStateChange(openmedia::core::StateChangeCallback) override {}
        void OnError(openmedia::core::ErrorCallback) override {}
    };

    auto sink = std::make_shared<FrameSink>();
    mixer->Connect(sink);

    // Wait for the mixed frame to be pushed to the sink
    for (int i = 0; i < 100; ++i) {
        if (sink->frameReceived) break;
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }

    if (sink->frameReceived && sink->outputFrame) {
        std::cout << "Mixed frame successfully generated!" << std::endl;
        SaveFrameToRaw(sink->outputFrame, "mixed_output.raw");
    } else {
        std::cerr << "Failed to mix frames." << std::endl;
    }

    // --- Cleanup ---
    mixer->Disconnect();
    mixer->Stop();
    std::cout << "--- Test Completed ---" << std::endl;
    return 0;
}
