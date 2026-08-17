#include <gtest/gtest.h>
#include <openmedia/io/MediaReader.h>

#include <cstdlib>
#include <filesystem>

extern "C" {
#include <libavcodec/avcodec.h>
}

using namespace openmedia::io;
using namespace openmedia::core;

class MediaReaderTest : public ::testing::Test {
protected:
    static void SetUpTestSuite() {
        // Ensure ffmpeg is in PATH or current dir (our setup script puts it in dist/demo/bin)
        std::string ffmpegPath = "..\\..\\dist\\demo\\bin\\ffmpeg.exe";
        if (std::filesystem::exists(ffmpegPath)) {
            std::string cmd = ffmpegPath + " -f lavfi -i testsrc=duration=1:size=640x360:rate=30 -c:v libx264 -pix_fmt yuv420p -y test_video.mp4 > NUL 2>&1";
            std::system(cmd.c_str());
        }
    }

    static void TearDownTestSuite() {
        if (std::filesystem::exists("test_video.mp4")) {
            std::filesystem::remove("test_video.mp4");
        }
    }
};

TEST_F(MediaReaderTest, OpenNonExistentFile) {
    MediaReader reader;
    auto result = reader.Open("nonexistent_file.mp4");
    EXPECT_FALSE(result.has_value());
    EXPECT_EQ(result.error().code, ErrorCode::FileNotFound);
}

TEST_F(MediaReaderTest, OpenAndReadStreams) {
    if (!std::filesystem::exists("test_video.mp4")) {
        GTEST_SKIP() << "test_video.mp4 not generated";
    }

    MediaReader reader;
    auto result = reader.Open("test_video.mp4");
    ASSERT_TRUE(result.has_value()) << result.error().message;

    auto streams = reader.GetStreams();
    EXPECT_FALSE(streams.empty());

    int videoIndex = reader.GetBestVideoStreamIndex();
    EXPECT_GE(videoIndex, 0);

    bool foundVideo = false;
    for (const auto& stream : streams) {
        if (stream.type == MediaType::Video) {
            foundVideo = true;
            EXPECT_EQ(stream.width, 640u);
            EXPECT_EQ(stream.height, 360u);
            EXPECT_DOUBLE_EQ(stream.frameRate, 30.0);
            EXPECT_NEAR(stream.durationSeconds, 1.0, 0.1);
            EXPECT_FALSE(stream.codecName.empty());
        }
    }
    EXPECT_TRUE(foundVideo);
}

TEST_F(MediaReaderTest, ReadPackets) {
    if (!std::filesystem::exists("test_video.mp4")) {
        GTEST_SKIP() << "test_video.mp4 not generated";
    }

    MediaReader reader;
    ASSERT_TRUE(reader.Open("test_video.mp4").has_value());

    AVPacket* pkt = av_packet_alloc();
    ASSERT_NE(pkt, nullptr);

    int packetCount = 0;
    while (true) {
        auto result = reader.ReadPacket(pkt);
        if (!result) {
            EXPECT_EQ(result.error().code, ErrorCode::EndOfStream);
            break;
        }
        packetCount++;
        av_packet_unref(pkt);
    }

    av_packet_free(&pkt);
    
    // 1 second at 30 fps should be roughly 30 packets
    EXPECT_GE(packetCount, 25);
    EXPECT_LE(packetCount, 35);
}
