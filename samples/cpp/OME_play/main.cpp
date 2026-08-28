#include <iostream>
#include <memory>
#include <thread>
#include <chrono>
#include <windows.h>

#include <openmedia/io/FileSource.h>
#include <openmedia/rendering/Preview.h>
#include <openmedia/audio/AudioPlayer.h>
#include <openmedia/audio/AudioMeter.h>
#include <openmedia/core/AVSyncClock.h>

using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::rendering;
using namespace openmedia::audio;
using namespace OpenMedia::Audio;

// Global pointers for WndProc
std::shared_ptr<Preview> g_preview;
std::shared_ptr<AudioPlayer> g_audioPlayer;
ScaleMode g_currentMode = ScaleMode::AspectRatioFit;

// Win32 Window Procedure
LRESULT CALLBACK WndProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam) {
    switch (uMsg) {
        case WM_SIZE: {
            if (g_preview) {
                int width = LOWORD(lParam);
                int height = HIWORD(lParam);
                g_preview->OnResize(width, height);
            }
            return 0;
        }
        case WM_KEYDOWN: {
            if (wParam == 'S') {
                g_currentMode = (g_currentMode == ScaleMode::Stretch) ? ScaleMode::AspectRatioFit : ScaleMode::Stretch;
                if (g_preview) {
                    g_preview->SetScaleMode(g_currentMode);
                    std::cout << "\n[Scale Mode] Toggled to: " 
                              << (g_currentMode == ScaleMode::Stretch ? "Stretch" : "AspectRatioFit") << std::endl;
                }
            }
            return 0;
        }
        case WM_CLOSE: {
            if (g_audioPlayer) {
                g_audioPlayer->Stop();
            }
            DestroyWindow(hwnd);
            return 0;
        }
        case WM_DESTROY:
            PostQuitMessage(0);
            return 0;
        default:
            return DefWindowProcA(hwnd, uMsg, wParam, lParam);
    }
}

// Window creation helper
HWND CreatePlayerWindow(int width, int height) {
    const char* CLASS_NAME = "OMEPlayWindowClass";
    
    WNDCLASSA wc = {};
    wc.lpfnWndProc = WndProc;
    wc.hInstance = GetModuleHandle(nullptr);
    wc.lpszClassName = CLASS_NAME;
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    
    RegisterClassA(&wc);
    
    HWND hwnd = CreateWindowExA(
        0, CLASS_NAME, "OpenMedia SDK - C++ Preview & Audio Meter",
        WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, width, height,
        nullptr, nullptr, GetModuleHandle(nullptr), nullptr
    );
    
    if (hwnd) {
        ShowWindow(hwnd, SW_SHOW);
        UpdateWindow(hwnd);
    }
    
    return hwnd;
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cerr << "Usage: OME_play <video_file_path>" << std::endl;
        return 1;
    }

    std::string filePath = argv[1];
    std::cout << "OME_play: Playing file: " << filePath << std::endl;

    // Create Win32 window
    HWND hwnd = CreatePlayerWindow(1280, 720);
    if (!hwnd) {
        std::cerr << "Failed to create Win32 window" << std::endl;
        return 1;
    }

    // 1. Create components
    auto fileSource = std::make_shared<FileSource>();
    
    // Open the video file and check for errors immediately
    auto openRes = fileSource->Open(filePath);
    if (!openRes.has_value()) {
        std::cerr << "Failed to open video file: " << filePath << std::endl;
        std::cerr << "Error: " << openRes.error().message << std::endl;
        if (hwnd) DestroyWindow(hwnd);
        return 1;
    }

    // Create Preview component instead of D3D11Renderer
    auto preview = std::make_shared<Preview>((void*)hwnd);
    g_preview = preview;

    // Create AudioPlayer and AudioMeter
    auto audioPlayer = std::make_shared<AudioPlayer>();
    g_audioPlayer = audioPlayer;
    AudioMeter audioMeter;

    // 2. Initialize components
    if (!fileSource->Initialize()) {
        std::cerr << "Failed to initialize FileSource" << std::endl;
        if (hwnd) DestroyWindow(hwnd);
        return 1;
    }

    // Initialize Preview
    if (!preview->Initialize().has_value()) {
        std::cerr << "Failed to initialize Preview renderer" << std::endl;
        if (hwnd) DestroyWindow(hwnd);
        return 1;
    }
    
    // Explicitly set default mode
    preview->SetScaleMode(g_currentMode);

    // Initialize AudioPlayer (FileSource always resamples audio to 48000 Hz)
    int audioSampleRate = 48000;
    int audioChannels = 2;
    auto streams = fileSource->GetStreams();
    if (!audioPlayer->Initialize(audioSampleRate, audioChannels).has_value()) {
        std::cerr << "Warning: Failed to initialize AudioPlayer. Playback will have no audio." << std::endl;
    }

    // 3. Start components
    fileSource->Start();

    // Determine FPS / frame duration for real-time pacing
    double frameDuration = 1.0 / 30.0; // Default to 30 FPS
    for (const auto& stream : streams) {
        if (stream.type == MediaType::Video && stream.frameRate > 0.0) {
            frameDuration = 1.0 / stream.frameRate;
            std::cout << "Detected video: " << stream.width << "x" << stream.height 
                      << " @ " << stream.frameRate << " FPS" << std::endl;
            break;
        }
    }

    std::cout << "Playback started. Commands:\n"
              << "  Press [S] key to toggle Stretch / AspectRatioFit display modes.\n"
              << "  Close window to exit.\n" << std::endl;

    SetConsoleCtrlHandler([](DWORD type) -> BOOL {
        if (type == CTRL_C_EVENT || type == CTRL_CLOSE_EVENT || type == CTRL_BREAK_EVENT) {
            if (g_audioPlayer) g_audioPlayer->Stop();
            return TRUE;
        }
        return FALSE;
    }, TRUE);

    std::atexit([]() {
        if (g_audioPlayer) g_audioPlayer->Stop();
    });

    AVSyncClock syncClock;
    std::shared_ptr<MediaFrame> pendingVideoFrame;

    MSG msg = {};
    bool playing = true;

    // 5. Run Win32 message + media rendering loop
    while (playing && fileSource->GetState() == PipelineState::Running) {
        // Process window messages
        while (PeekMessage(&msg, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
            if (msg.message == WM_QUIT) {
                playing = false;
                break;
            }
        }
        if (!playing) break;

        // Pull audio with pacing (target 250ms audio queue in XAudio2)
        constexpr double TARGET_AUDIO_QUEUE_SEC = 0.250;
        if (audioPlayer->GetQueuedDurationSeconds() < TARGET_AUDIO_QUEUE_SEC) {
            auto audioResult = fileSource->PullAudioFrame();
            if (audioResult && *audioResult) {
                auto audioFrame = *audioResult;
                audioPlayer->PlayFrame(audioFrame);

                // Compute audio meter levels
                audioMeter.ProcessSamples(audioFrame.get());
                auto channelData = audioMeter.GetChannelData();
                if (!channelData.empty()) {
                    float rms = channelData[0].rms_db; // Channel 0 (Left)
                    
                    // Normalize RMS dB range (-60dB to 0dB) to 0.0 - 1.0
                    float normalized = (rms + 60.0f) / 60.0f;
                    if (normalized < 0.0f) normalized = 0.0f;
                    if (normalized > 1.0f) normalized = 1.0f;
                    
                    int barLength = static_cast<int>(normalized * 25);
                    std::string bar(barLength, '=');
                    std::string spaces(25 - barLength, ' ');
                    std::cout << "\rAudio Volume Bar: [" << bar << spaces << "] " << (int)rms << " dB   " << std::flush;
                }
            } else if (audioResult.error().code == ErrorCode::EndOfStream) {
                // Do not break immediately, video might still have frames
            }
        }

        // Update audio clock from hardware
        syncClock.UpdateAudioClock(audioPlayer->GetPlaybackPositionSeconds());

        // Sync video to audio clock
        if (!pendingVideoFrame) {
            auto frameResult = fileSource->PullVideoFrame();
            if (frameResult && *frameResult) {
                pendingVideoFrame = *frameResult;
            } else if (!frameResult && frameResult.error().code == ErrorCode::EndOfStream) {
                std::cout << "\nEnd of video stream reached." << std::endl;
                playing = false;
                break;
            } else if (!frameResult) {
                std::cerr << "\nError pulling video frame: " << frameResult.error().message << std::endl;
                playing = false;
                break;
            }
        }

        if (pendingVideoFrame) {
            auto action = syncClock.EvaluateVideoFrame(*pendingVideoFrame);
            switch (action) {
                case AVSyncClock::VideoAction::Display:
                    preview->DisplayFrame(pendingVideoFrame);
                    pendingVideoFrame = nullptr;
                    break;
                case AVSyncClock::VideoAction::Drop:
                    pendingVideoFrame = nullptr;  // skip, pull next
                    break;
                case AVSyncClock::VideoAction::Wait:
                    break;  // hold frame, try again next iteration
            }
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }

    // 6. Stop and cleanup
    std::cout << "\nStopping pipeline..." << std::endl;
    fileSource->Stop();
    audioPlayer->Stop();

    fileSource->Close();

    if (hwnd) {
        DestroyWindow(hwnd);
    }

    std::cout << "Pipeline stopped successfully." << std::endl;
    return 0;
}
