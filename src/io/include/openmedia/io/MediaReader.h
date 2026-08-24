#pragma once
#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/Types.h>
#include <string>
#include <vector>
#include <memory>
#include <cstdint>
#include <expected>

struct AVFormatContext;
struct AVPacket;
struct AVFrame;

namespace openmedia::io {

struct StreamInfo {
    int index = -1;
    core::MediaType type = core::MediaType::Unknown;
    std::string codecName;
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t sampleRate = 0;
    uint32_t channels = 0;
    double durationSeconds = 0.0;
    double frameRate = 0.0;
    core::Rational timeBase = {0, 1};
};

class MediaReader {
public:
    MediaReader();
    ~MediaReader();

    MediaReader(const MediaReader&) = delete;
    MediaReader& operator=(const MediaReader&) = delete;

    core::VoidResult Open(const std::string& path, const std::string& format = "");
    void Close();

    const std::vector<StreamInfo>& GetStreams() const;
    int GetBestVideoStreamIndex() const;
    int GetBestAudioStreamIndex() const;

    core::VoidResult ReadVideoFrame(AVFrame* frame);
    core::VoidResult ReadAudioFrame(AVFrame* frame);
    core::VoidResult ReadPacket(AVPacket* packet);
    
    core::VoidResult Seek(double timestampSeconds);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::io

