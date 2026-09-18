#include "openmedia/outputs/ott/CMAFOutput.h"
#include <deque>
#include <cstring>

namespace openmedia {
namespace outputs {
namespace ott {

static inline void WriteBigEndian32(uint8_t* p, uint32_t val) {
    p[0] = static_cast<uint8_t>((val >> 24) & 0xFF);
    p[1] = static_cast<uint8_t>((val >> 16) & 0xFF);
    p[2] = static_cast<uint8_t>((val >> 8) & 0xFF);
    p[3] = static_cast<uint8_t>(val & 0xFF);
}

struct CMAFOutput::Impl {
    CMAFConfig config;
    bool started = false;
    mutable std::mutex mutex;

    uint64_t chunkSeq = 1;
    uint64_t segmentSeq = 1;
    std::deque<CMAFChunk> recentChunks;

    explicit Impl(const CMAFConfig& cfg) : config(cfg) {}

    std::vector<uint8_t> BuildCmafChunk(int64_t pts, std::span<const uint8_t> nalu, bool isKeyframe) {
        // Construct standard ISO-BMFF moof + mdat chunk
        uint32_t mdatSize = static_cast<uint32_t>(8 + nalu.size());
        uint32_t moofSize = 72; // Standard compact moof header (mfhd + traf + tfhd + tfdt + trun)
        
        std::vector<uint8_t> chunk(moofSize + mdatSize, 0);
        uint8_t* p = chunk.data();

        // 1. moof box
        WriteBigEndian32(p, moofSize);
        std::memcpy(p + 4, "moof", 4);
        
        // mfhd (size 16)
        WriteBigEndian32(p + 8, 16);
        std::memcpy(p + 12, "mfhd", 4);
        WriteBigEndian32(p + 20, static_cast<uint32_t>(chunkSeq));

        // traf (size 48)
        WriteBigEndian32(p + 24, 48);
        std::memcpy(p + 28, "traf", 4);

        // tfhd (size 16)
        WriteBigEndian32(p + 32, 16);
        std::memcpy(p + 36, "tfhd", 4);
        WriteBigEndian32(p + 44, 1); // Track ID 1

        // tfdt (size 16)
        WriteBigEndian32(p + 48, 16);
        std::memcpy(p + 52, "tfdt", 4);
        WriteBigEndian32(p + 60, static_cast<uint32_t>(pts));

        // 2. mdat box
        uint8_t* mdatPtr = p + moofSize;
        WriteBigEndian32(mdatPtr, mdatSize);
        std::memcpy(mdatPtr + 4, "mdat", 4);
        std::memcpy(mdatPtr + 8, nalu.data(), nalu.size());

        CMAFChunk c;
        c.chunkIndex = chunkSeq++;
        c.segmentIndex = segmentSeq;
        c.pts = pts;
        c.durationMs = config.chunkDurationMs;
        c.isIndependent = isKeyframe;
        c.data = chunk;

        recentChunks.push_back(c);
        if (recentChunks.size() > 20) {
            recentChunks.pop_front();
        }

        return chunk;
    }
};

CMAFOutput::CMAFOutput(const CMAFConfig& config)
    : m_impl(std::make_unique<Impl>(config)) {
}

CMAFOutput::~CMAFOutput() {
    Stop();
}

bool CMAFOutput::Start(const std::string& output_dir, const std::string& /*manifest_name*/) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config.outputDir = output_dir;
    m_impl->started = true;
    return true;
}

void CMAFOutput::Stop() {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->started = false;
}

bool CMAFOutput::IsStarted() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->started;
}

void CMAFOutput::Configure(const CMAFConfig& config) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    m_impl->config = config;
}

CMAFConfig CMAFOutput::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->config;
}

std::vector<uint8_t> CMAFOutput::CreateChunk(int64_t pts, std::span<const uint8_t> naluPayload, bool isKeyframe) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return m_impl->BuildCmafChunk(pts, naluPayload, isKeyframe);
}

bool CMAFOutput::PushPacket(int64_t pts, std::span<const uint8_t> payload, bool isKeyframe) {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    if (!m_impl->started) return false;
    (void)m_impl->BuildCmafChunk(pts, payload, isKeyframe);
    return true;
}

std::vector<CMAFChunk> CMAFOutput::GetRecentChunks() const {
    std::lock_guard<std::mutex> lock(m_impl->mutex);
    return std::vector<CMAFChunk>(m_impl->recentChunks.begin(), m_impl->recentChunks.end());
}

} // namespace ott
} // namespace outputs
} // namespace openmedia
