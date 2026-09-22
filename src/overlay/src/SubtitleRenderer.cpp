#include <openmedia/overlay/SubtitleRenderer.h>
#include <fstream>
#include <sstream>
#include <regex>
#include <algorithm>

namespace openmedia::overlay {

static int64_t ParseTimecodeToMs(int h, int m, int s, int ms) {
    return static_cast<int64_t>(h) * 3600000LL +
           static_cast<int64_t>(m) * 60000LL +
           static_cast<int64_t>(s) * 1000LL +
           ms;
}

SubtitleRenderer::SubtitleRenderer() = default;
SubtitleRenderer::~SubtitleRenderer() = default;

bool SubtitleRenderer::LoadSubtitleFile(const std::string& filepath) {
    std::ifstream file(filepath);
    if (!file.is_open()) return false;

    std::stringstream buffer;
    buffer << file.rdbuf();
    return LoadSubtitleContent(buffer.str());
}

bool SubtitleRenderer::LoadSubtitleContent(const std::string& content) {
    m_cues.clear();
    std::istringstream stream(content);
    std::string line;

    // Pattern matching: 00:01:20,000 --> 00:01:23,000 or 00:01:20.000 --> 00:01:23.000
    std::regex timeRegex(R"((\d{1,2}):(\d{2}):(\d{2})[,\.](\d{3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,\.](\d{3}))");

    SubtitleCue currentCue;
    bool inCue = false;

    while (std::getline(stream, line)) {
        // Strip trailing \r
        if (!line.empty() && line.back() == '\r') {
            line.pop_back();
        }

        std::smatch match;
        if (std::regex_search(line, match, timeRegex)) {
            if (inCue && !currentCue.text.empty()) {
                m_cues.push_back(currentCue);
            }

            currentCue.startMs = ParseTimecodeToMs(
                std::stoi(match[1].str()), std::stoi(match[2].str()),
                std::stoi(match[3].str()), std::stoi(match[4].str()));

            currentCue.endMs = ParseTimecodeToMs(
                std::stoi(match[5].str()), std::stoi(match[6].str()),
                std::stoi(match[7].str()), std::stoi(match[8].str()));

            currentCue.text.clear();
            inCue = true;
        } else if (inCue) {
            if (line.empty()) {
                if (!currentCue.text.empty()) {
                    m_cues.push_back(currentCue);
                    currentCue = SubtitleCue{};
                    inCue = false;
                }
            } else {
                // Check if line is just an index number (e.g. "1")
                bool isIndex = std::all_of(line.begin(), line.end(), ::isdigit);
                if (!isIndex) {
                    if (!currentCue.text.empty()) currentCue.text += "\n";
                    currentCue.text += line;
                }
            }
        }
    }

    if (inCue && !currentCue.text.empty()) {
        m_cues.push_back(currentCue);
    }

    return !m_cues.empty();
}

void SubtitleRenderer::SetCurrentText(const std::string& text) {
    m_currentText = text;
}

std::string SubtitleRenderer::GetTextAtTimeMs(int64_t timeMs) const {
    for (const auto& cue : m_cues) {
        if (timeMs >= cue.startMs && timeMs <= cue.endMs) {
            return cue.text;
        }
    }
    return {};
}

void SubtitleRenderer::SetStyle(int fontSize, uint32_t textColor, uint32_t bgColor) {
    m_fontSize = fontSize;
    m_textColor = textColor;
    m_bgColor = bgColor;
}

const std::vector<SubtitleCue>& SubtitleRenderer::GetCues() const {
    return m_cues;
}

bool SubtitleRenderer::Render(std::shared_ptr<openmedia::core::MediaFrame> frame) {
    if (!frame) return false;

    // Convert frame PTS (in 90kHz ticks) to milliseconds
    int64_t pts = frame->GetPts();
    int64_t timeMs = (pts > 0) ? (pts / 90) : 0;

    std::string textToDisplay = GetTextAtTimeMs(timeMs);
    if (textToDisplay.empty()) {
        textToDisplay = m_currentText;
    }

    if (textToDisplay.empty()) return true; // Nothing to render for this frame

    // Tag frame metadata with active subtitle text
    frame->GetMetadata().Set("subtitle_text", textToDisplay);

    // If frame is BGRA format, draw a semi-transparent subtitle background box at bottom
    if (frame->GetPixelFormat() == core::PixelFormat::BGRA) {
        uint8_t* pixels = frame->GetVideoPlane(0);
        int stride = frame->GetLineSize(0);
        int width = static_cast<int>(frame->GetWidth());
        int height = static_cast<int>(frame->GetHeight());

        if (pixels && stride > 0 && width > 0 && height > 0) {
            int boxHeight = std::min(m_fontSize * 2 + 20, height / 5);
            int startY = height - boxHeight - 20;
            if (startY < 0) startY = 0;

            uint8_t bgB = static_cast<uint8_t>((m_bgColor >> 0) & 0xFF);
            uint8_t bgG = static_cast<uint8_t>((m_bgColor >> 8) & 0xFF);
            uint8_t bgR = static_cast<uint8_t>((m_bgColor >> 16) & 0xFF);
            uint8_t bgA = static_cast<uint8_t>((m_bgColor >> 24) & 0xFF);

            float alpha = bgA / 255.0f;
            float invAlpha = 1.0f - alpha;

            for (int y = startY; y < startY + boxHeight && y < height; ++y) {
                uint8_t* row = pixels + (y * stride);
                for (int x = width / 8; x < width * 7 / 8; ++x) {
                    row[x * 4 + 0] = static_cast<uint8_t>(row[x * 4 + 0] * invAlpha + bgB * alpha);
                    row[x * 4 + 1] = static_cast<uint8_t>(row[x * 4 + 1] * invAlpha + bgG * alpha);
                    row[x * 4 + 2] = static_cast<uint8_t>(row[x * 4 + 2] * invAlpha + bgR * alpha);
                }
            }
        }
    }

    return true;
}

} // namespace openmedia::overlay
