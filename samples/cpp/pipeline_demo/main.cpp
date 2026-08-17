#include <openmedia/io/FileSource.h>
#include <openmedia/mixer/Mixer.h>
#include <openmedia/mixer/MixerLayer.h>
#include <openmedia/rendering/Preview.h>
#include <openmedia/audio/AudioMixer.h>
#include <openmedia/audio/AudioEngine.h>
#include <openmedia/audio/AudioPlayer.h>
#include <openmedia/core/Logger.h>
#include <openmedia/core/IMediaObject.h>
#include <windows.h>
#include <iostream>
#include <thread>
#include <chrono>

using namespace openmedia;

bool g_running = true;

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    if (uMsg == WM_DESTROY) {
        PostQuitMessage(0);
        g_running = false;
        return 0;
    }
    return DefWindowProcW(hwnd, uMsg, wParam, lParam);
}

class PreviewSink : public core::IMediaObject {
public:
    PreviewSink(rendering::Preview& preview) : m_preview(preview) {}
    std::string GetName() const override { return "PreviewSink"; }
    core::PipelineState GetState() const override { return m_state; }
    core::VoidResult Initialize() override { return {}; }
    core::VoidResult Start() override { m_state = core::PipelineState::Running; return {}; }
    core::VoidResult Stop() override { m_state = core::PipelineState::Stopped; return {}; }
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override {
        if (frame) m_preview.DisplayFrame(frame);
        return {};
    }
    core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override { return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "")); }
    core::VoidResult Connect(std::shared_ptr<core::IMediaObject>) override { return {}; }
    core::VoidResult Disconnect() override { return {}; }
    void OnStateChange(core::StateChangeCallback) override {}
    void OnError(core::ErrorCallback) override {}
private:
    rendering::Preview& m_preview;
    core::PipelineState m_state = core::PipelineState::Idle;
};

class AudioSink : public core::IMediaObject {
public:
    AudioSink(audio::AudioPlayer& player) : m_player(player) {}
    std::string GetName() const override { return "AudioSink"; }
    core::PipelineState GetState() const override { return m_state; }
    core::VoidResult Initialize() override { return {}; }
    core::VoidResult Start() override { m_state = core::PipelineState::Running; return {}; }
    core::VoidResult Stop() override { m_state = core::PipelineState::Stopped; return {}; }
    core::VoidResult PushFrame(std::shared_ptr<core::MediaFrame> frame) override {
        if (frame) m_player.PlayFrame(frame);
        return {};
    }
    core::Result<std::shared_ptr<core::MediaFrame>> PullFrame() override { return std::unexpected(core::Error::Make(core::ErrorCode::NotSupported, "")); }
    core::VoidResult Connect(std::shared_ptr<core::IMediaObject>) override { return {}; }
    core::VoidResult Disconnect() override { return {}; }
    void OnStateChange(core::StateChangeCallback) override {}
    void OnError(core::ErrorCallback) override {}
private:
    audio::AudioPlayer& m_player;
    core::PipelineState m_state = core::PipelineState::Idle;
};

int main() {
    openmedia::core::Logger::SInfo("OME", "Starting OpenMedia Pipeline Demo with Audio");

    LPCWSTR CLASS_NAME = L"OpenMediaPreviewClass";
    WNDCLASSW wc = {};
    wc.lpfnWndProc = WindowProc;
    wc.hInstance = GetModuleHandle(nullptr);
    wc.lpszClassName = CLASS_NAME;
    RegisterClassW(&wc);

    RECT rect = { 0, 0, 1280, 720 };
    AdjustWindowRect(&rect, WS_OVERLAPPEDWINDOW, FALSE);
    HWND hwnd = CreateWindowExW(
        0, CLASS_NAME, L"OpenMedia Pipeline Preview",
        WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, rect.right - rect.left, rect.bottom - rect.top,
        nullptr, nullptr, wc.hInstance, nullptr);

    ShowWindow(hwnd, SW_SHOW);

    io::FileSource fileSource;
    auto resOpen = fileSource.Open("sample.mp4");
    if (!resOpen.has_value()) {
        openmedia::core::Logger::SWarn("OME", "Could not open video file.");
    }
    (void)fileSource.Start();

    mixer::Mixer mixer;
    (void)mixer.Initialize();
    (void)mixer.SetOutputFormat(1280, 720, 60);
    
    auto layer1 = std::make_shared<mixer::MixerLayer>(1);
    layer1->SetSize(1280, 720);
    mixer.AddLayer(layer1);
    (void)layer1->Start();

    auto engine = std::make_shared<audio::AudioEngine>();
    engine->Initialize(48000, core::ChannelLayout::Stereo, core::SampleFormat::Float32);
    audio::AudioMixer audioMixer(engine);
    (void)audioMixer.Initialize();
    audioMixer.AddInput(1);

    audio::AudioPlayer audioPlayer;
    auto audioInit = audioPlayer.Initialize();
    if (!audioInit.has_value()) {
        openmedia::core::Logger::SError("OME", "AudioPlayer init failed: {}", audioInit.error().message);
    }

    rendering::Preview preview(hwnd);
    (void)preview.Initialize();

    auto previewSink = std::make_shared<PreviewSink>(preview);
    mixer.Connect(previewSink);
    (void)mixer.Start();

    auto audioSink = std::make_shared<AudioSink>(audioPlayer);
    audioMixer.Connect(audioSink);
    (void)audioMixer.Start();

    MSG msg = {};
    
    std::thread mediaThread([&]() {
        double videoTime = 0.0;
        double audioTime = 0.0;
        auto startTime = std::chrono::steady_clock::now();

        while (g_running) {
            auto now = std::chrono::steady_clock::now();
            double elapsed = std::chrono::duration<double>(now - startTime).count();

            // Pull and push video to keep up with real time
            while (videoTime <= elapsed && g_running) {
                auto resVideo = fileSource.PullVideoFrame();
                if (resVideo.has_value()) {
                    layer1->PushFrame(resVideo.value());
                    // Assume ~30fps for standard fallback if we don't extract it
                    videoTime += 1.0 / 30.0; 
                } else {
                    break;
                }
            }

            // Pull and push audio to keep up with real time
            while (audioTime <= elapsed && g_running) {
                auto resAudio = fileSource.PullAudioFrame();
                if (resAudio.has_value()) {
                    auto currentAudio = resAudio.value();
                    audioMixer.PushFrame(currentAudio, 1);
                    audioTime += static_cast<double>(currentAudio->GetSampleCount()) / 48000.0;
                } else {
                    break;
                }
            }

            std::this_thread::sleep_for(std::chrono::milliseconds(5));
        }
    });

    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    g_running = false;
    if (mediaThread.joinable()) {
        mediaThread.join();
    }

    (void)mixer.Stop();
    (void)audioMixer.Stop();

    return 0;
}
