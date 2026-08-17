#include <openmedia/mixer/Mixer.h>
#include <openmedia/mixer/MixerLayer.h>
#include <openmedia/core/Logger.h>
#include <algorithm>
#include <chrono>

namespace openmedia::mixer {

Mixer::Mixer() : m_outputWidth(1920), m_outputHeight(1080), m_fps(60), m_state(core::PipelineState::Stopped), m_stopRequested(false) {}

Mixer::~Mixer() {
    (void)Stop();
}

core::Result<void> Mixer::SetOutputFormat(int outputWidth, int outputHeight, int fps) {
    std::lock_guard lock(m_mutex);
    if (m_state != core::PipelineState::Stopped) {
        return std::unexpected(core::Error::Make(core::ErrorCode::InvalidState, "Cannot change format while running"));
    }
    m_outputWidth = outputWidth;
    m_outputHeight = outputHeight;
    m_fps = fps;
    return {};
}

std::string Mixer::GetName() const {
    return "Mixer";
}

core::PipelineState Mixer::GetState() const {
    std::lock_guard lock(m_mutex);
    return m_state;
}

core::VoidResult Mixer::Initialize() {
    std::lock_guard lock(m_mutex);
    m_layers.clear();
    
    if (m_gpuContext) {
        // Assume shaders are next to the executable in a shaders/ folder
        auto res = m_gpuContext->InitMixerPipeline("shaders/MixerVertexShader.hlsl", "shaders/MixerPixelShader.hlsl");
        if (!res.has_value()) {
            core::Logger::SError("Mixer", "Failed to initialize GPU Mixer Pipeline: {}", res.error().message);
        }
    }
    
    return {};
}

core::VoidResult Mixer::Start() {
    std::lock_guard lock(m_mutex);
    if (m_state == core::PipelineState::Running) return {};

    m_stopRequested = false;
    auto oldState = m_state;
    m_state = core::PipelineState::Running;
    
    // Start mixer loop thread
    m_mixThread = std::thread(&Mixer::MixThreadLoop, this);
    
    if (m_onStateChange) m_onStateChange(oldState, m_state);
    return {};
}

core::VoidResult Mixer::Stop() {
    core::PipelineState oldState;
    {
        std::lock_guard lock(m_mutex);
        if (m_state == core::PipelineState::Stopped) return {};
        m_stopRequested = true;
        oldState = m_state;
    }
    
    if (m_mixThread.joinable()) {
        m_mixThread.join();
    }
    
    std::lock_guard lock(m_mutex);
    m_state = core::PipelineState::Stopped;
    if (m_onStateChange) m_onStateChange(oldState, m_state);
    return {};
}

core::VoidResult Mixer::PushFrame(std::shared_ptr<core::MediaFrame> /*frame*/) {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "PushFrame not supported on Mixer"));
}

core::Result<std::shared_ptr<core::MediaFrame>> Mixer::PullFrame() {
    return std::unexpected(core::Error::Make(core::ErrorCode::NotImplemented, "PullFrame via API not implemented (Mixer pushes downstream)"));
}

core::VoidResult Mixer::Connect(std::shared_ptr<core::IMediaObject> downstream) {
    std::lock_guard lock(m_mutex);
    m_downstream = downstream;
    return {};
}

core::VoidResult Mixer::Disconnect() {
    std::lock_guard lock(m_mutex);
    m_downstream.reset();
    return {};
}

void Mixer::OnStateChange(core::StateChangeCallback callback) {
    std::lock_guard lock(m_mutex);
    m_onStateChange = std::move(callback);
}

void Mixer::OnError(core::ErrorCallback callback) {
    std::lock_guard lock(m_mutex);
    m_onError = std::move(callback);
}

core::Result<void> Mixer::AddLayer(std::shared_ptr<MixerLayer> layer) {
    if (!layer) return std::unexpected(core::Error::Make(core::ErrorCode::InvalidArgument, "Layer is null"));
    std::lock_guard lock(m_mutex);
    m_layers.push_back(layer);
    
    // Sort layers by Z-Index
    std::sort(m_layers.begin(), m_layers.end(), [](const auto& a, const auto& b) {
        return a->GetZIndex() < b->GetZIndex();
    });
    
    return {};
}

core::Result<void> Mixer::RemoveLayer(int layerId) {
    std::lock_guard lock(m_mutex);
    auto it = std::remove_if(m_layers.begin(), m_layers.end(), 
        [layerId](const std::shared_ptr<MixerLayer>& l) { return l->GetId() == layerId; });
    if (it != m_layers.end()) {
        m_layers.erase(it, m_layers.end());
    }
    return {};
}

void Mixer::MixThreadLoop() {
    auto frameInterval = std::chrono::microseconds(1000000 / m_fps);
    auto nextFrameTime = std::chrono::steady_clock::now();
    
    int64_t pts = 0;

    while (!m_stopRequested) {
        auto now = std::chrono::steady_clock::now();
        if (now >= nextFrameTime) {
            // Render a frame
            auto mixedFrame = core::MediaFrame::CreateVideo(m_outputWidth, m_outputHeight, core::PixelFormat::BGRA);
            mixedFrame->SetPts(pts++);
            
            if (m_gpuContext) {
                RenderFrameGPU(mixedFrame);
            } else {
                RenderFrame(mixedFrame);
            }
            
            std::shared_ptr<core::IMediaObject> downstream;
            {
                std::lock_guard lock(m_mutex);
                downstream = m_downstream;
            }
            if (downstream) {
                (void)downstream->PushFrame(mixedFrame);
            }
            
            nextFrameTime += frameInterval;
            if (nextFrameTime < now) {
                // We're falling behind, skip ahead
                nextFrameTime = now + frameInterval;
            }
        } else {
            // Sleep for a short time
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
    }
}

void Mixer::RenderFrame(std::shared_ptr<core::MediaFrame>& outputFrame) {
    std::lock_guard lock(m_mutex);
    
    // Clear output frame to black (BGRA = 0, 0, 0, 255)
    uint8_t* outData = outputFrame->GetVideoPlane(0);
    int outStride = outputFrame->GetLineSize(0);
    for (int y = 0; y < m_outputHeight; ++y) {
        uint8_t* row = outData + y * outStride;
        for (int x = 0; x < m_outputWidth; ++x) {
            row[x * 4 + 0] = 0;   // B
            row[x * 4 + 1] = 0;   // G
            row[x * 4 + 2] = 0;   // R
            row[x * 4 + 3] = 255; // A
        }
    }
    
    // Blend each layer
    for (const auto& layer : m_layers) {
        if (!layer->IsVisible() || layer->GetOpacity() <= 0.0f) continue;
        
        auto frame = layer->GetCurrentFrame();
        if (!frame) continue; // Layer has no frame yet
        
        // Very basic naive blending assuming frame is BGRA and matches layer bounds.
        // In a real CPU mixer, we'd do format conversion and scaling here.
        // For now, let's just do a simple copy/blend if it's BGRA.
        
        if (frame->GetPixelFormat() == core::PixelFormat::BGRA) {
            int lX = layer->GetX();
            int lY = layer->GetY();
            int lW = layer->GetWidth();
            int lH = layer->GetHeight();
            
            uint8_t* inData = frame->GetVideoPlane(0);
            int inStride = frame->GetLineSize(0);
            float opacity = layer->GetOpacity();
            
            for (int y = 0; y < lH; ++y) {
                int outY = lY + y;
                if (outY < 0 || outY >= m_outputHeight) continue;
                
                uint8_t* outRow = outData + outY * outStride;
                uint8_t* inRow = inData + y * inStride;
                
                for (int x = 0; x < lW; ++x) {
                    int outX = lX + x;
                    if (outX < 0 || outX >= m_outputWidth) continue;
                    
                    uint8_t inB = inRow[x * 4 + 0];
                    uint8_t inG = inRow[x * 4 + 1];
                    uint8_t inR = inRow[x * 4 + 2];
                    uint8_t inA = static_cast<uint8_t>(inRow[x * 4 + 3] * opacity);
                    
                    if (inA == 0) continue;
                    
                    if (inA == 255) {
                        outRow[outX * 4 + 0] = inB;
                        outRow[outX * 4 + 1] = inG;
                        outRow[outX * 4 + 2] = inR;
                        outRow[outX * 4 + 3] = 255;
                    } else {
                        // Alpha blending
                        float alpha = inA / 255.0f;
                        float invAlpha = 1.0f - alpha;
                        outRow[outX * 4 + 0] = static_cast<uint8_t>(inB * alpha + outRow[outX * 4 + 0] * invAlpha);
                        outRow[outX * 4 + 1] = static_cast<uint8_t>(inG * alpha + outRow[outX * 4 + 1] * invAlpha);
                        outRow[outX * 4 + 2] = static_cast<uint8_t>(inR * alpha + outRow[outX * 4 + 2] * invAlpha);
                        // Output alpha remains 255 since we blend onto an opaque background
                    }
                }
            }
        }
    }
}

void Mixer::RenderFrameGPU(std::shared_ptr<core::MediaFrame>& outputFrame) {
    std::lock_guard lock(m_mutex);

    if (!m_gpuContext) return;

    // Ping-pong buffers for mixing
    std::vector<uint8_t> bgData(m_outputWidth * m_outputHeight * 4, 0); // Black, Transparent
    void* texA = nullptr;
    void* texB = nullptr;
    m_gpuContext->UploadTexture(bgData.data(), m_outputWidth, m_outputHeight, core::PixelFormat::BGRA, &texA);
    m_gpuContext->UploadTexture(bgData.data(), m_outputWidth, m_outputHeight, core::PixelFormat::BGRA, &texB);

    void* currentBg = texA;
    void* currentTarget = texB;

    for (const auto& layer : m_layers) {
        if (!layer->IsVisible() || layer->GetOpacity() <= 0.0f) continue;
        
        auto frame = layer->GetCurrentFrame();
        if (!frame) continue; 
        
        void* layerHandle = nullptr;
        bool isTemp = false;
        
        // Ensure layer is on GPU (fallback to upload if not)
        // Assume BGRA for now
        if (frame->GetPixelFormat() == core::PixelFormat::BGRA) {
            m_gpuContext->UploadTexture(frame->GetVideoPlane(0), frame->GetWidth(), frame->GetHeight(), frame->GetPixelFormat(), &layerHandle);
            isTemp = true;
        }

        if (layerHandle) {
            m_gpuContext->ExecuteMixerPipeline(currentBg, layerHandle, currentTarget, layer->GetOpacity());
            std::swap(currentBg, currentTarget);
            
            if (isTemp) m_gpuContext->FreeTexture(layerHandle);
        }
    }

    // Download to CPU
    m_gpuContext->DownloadTexture(currentBg, outputFrame->GetVideoPlane(0), m_outputWidth, m_outputHeight, core::PixelFormat::BGRA);
    
    m_gpuContext->FreeTexture(texA);
    m_gpuContext->FreeTexture(texB);
}

} // namespace openmedia::mixer
