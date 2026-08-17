#include <iostream>
#include <memory>
#include <thread>
#include <chrono>

#include <openmedia/ndi/NDISource.h>
#include <openmedia/codecs/FFmpegH264Encoder.h>
#include <openmedia/srt/SRTOutput.h>

using namespace openmedia::core;
using namespace openmedia::ndi;
using namespace openmedia::srt;
using namespace openmedia::codecs;

int main(int argc, char* argv[]) {
    if (argc < 3) {
        std::cerr << "Usage: ndi_srt_output <ndi_source_name> <srt_destination_uri>" << std::endl;
        std::cerr << "Example: ndi_srt_output \"PC-NAME (OBS)\" \"srt://127.0.0.1:9000\"" << std::endl;
        return 1;
    }

    std::string ndiName = argv[1];
    std::string srtUri = argv[2];

    std::cout << "Bridging NDI source [" << ndiName << "] to SRT [" << srtUri << "]" << std::endl;

    // 1. Create components
    auto ndiReceiver = std::make_shared<NDISource>();
    auto h264Encoder = std::make_shared<FFmpegH264Encoder>();
    auto srtOutput = std::make_shared<SRTOutput>();

    // 2. Configure Encoder for streaming
    openmedia::codecs::EncoderConfig config;
    config.width = 1920;
    config.height = 1080;
    config.bitrate = 5000000;
    config.fps = 30;
    config.preset = "fast";
    h264Encoder->Configure(config);

    // 3. Initialize components
    if (!ndiReceiver->Connect(ndiName)) {
        std::cerr << "Failed to connect to NDI source: " << ndiName << std::endl;
        return 1;
    }

    if (!h264Encoder->Initialize()) {
        std::cerr << "Failed to initialize H264 Encoder" << std::endl;
        return 1;
    }

    if (!srtOutput->Start(srtUri)) {
        std::cerr << "Failed to start SRT output on: " << srtUri << std::endl;
        return 1;
    }

    // 4. Start pipeline (from sink to source)
    h264Encoder->Start();

    std::cout << "Bridge is running. Press Ctrl+C to stop..." << std::endl;

    // 5. Run loop indefinitely
    while (true) {
        std::this_thread::sleep_for(std::chrono::seconds(1));
    }

    // 6. Cleanup (Unreachable in this simple demo without signal handling)
    h264Encoder->Stop();
    srtOutput->Stop();
    ndiReceiver->Disconnect();

    return 0;
}
