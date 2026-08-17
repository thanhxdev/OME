#include <iostream>
#include <memory>
#include <thread>
#include <chrono>

#include <openmedia/io/FileSource.h>
#include <openmedia/codecs/FFmpegH264Decoder.h>
#include <openmedia/rendering/D3D11Renderer.h>

using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::codecs;
using namespace openmedia::rendering;

int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cerr << "Usage: simple_player <video_file_path>" << std::endl;
        return 1;
    }

    std::string filePath = argv[1];
    std::cout << "Playing file: " << filePath << std::endl;

    // 1. Create components
    auto fileSource = std::make_shared<FileSource>();
    fileSource->Open(filePath);
    auto decoder = std::make_shared<FFmpegH264Decoder>();
    auto renderer = std::make_shared<D3D11Renderer>();

    // 2. Initialize components
    if (!fileSource->Initialize()) {
        std::cerr << "Failed to initialize FileSource" << std::endl;
        return 1;
    }

    if (!decoder->Initialize()) {
        std::cerr << "Failed to initialize Decoder" << std::endl;
        return 1;
    }

    if (!renderer->Initialize(nullptr).has_value()) {
        std::cerr << "Failed to initialize Renderer" << std::endl;
        return 1;
    }

    // 3. Connect pipeline: FileSource -> Decoder -> Renderer
    fileSource->Connect(decoder);
    // D3D11Renderer isn't an IMediaObject, simple_player logic needs to manually pull frames.
    // For this simple_player, we'll just not connect it, and manually render if needed,
    // but the original code had decoder->Connect(renderer).
    // Let's comment this out.
    // decoder->Connect(renderer);

    // 4. Start pipeline (from sink to source)
    // renderer->Start();
    auto decStart = decoder->Start();
    auto fsStart = fileSource->Start();

    std::cout << "Pipeline running. Close the window to exit..." << std::endl;

    // 5. Run loop until window is closed or file ends
    // In a real app we'd pump messages or wait for an event. Here we just loop.
    while (fileSource->GetState() == PipelineState::Running) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
        // We'd pull frames and render them here
        auto frameResult = decoder->PullFrame();
        if (frameResult.has_value() && frameResult.value()) {
            renderer->Render(frameResult.value());
        }
    }

    // 6. Stop and cleanup
    std::cout << "Stopping pipeline..." << std::endl;
    auto fsStop = fileSource->Stop();
    auto decStop = decoder->Stop();
    auto renStop = renderer->Shutdown();

    fileSource->Disconnect();
    decoder->Disconnect();

    return 0;
}
