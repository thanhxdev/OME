#include <gtest/gtest.h>
#include <openmedia/io/FileSource.h>
#include <openmedia/core/MediaFrame.h>

#include <cstdlib>
#include <filesystem>
#include <thread>

using namespace openmedia::io;
using namespace openmedia::core;

class FileSourceTest : public ::testing::Test {
protected:
    static void SetUpTestSuite() {
        std::string ffmpegPath = "..\\..\\dist\\demo\\bin\\ffmpeg.exe";
        if (std::filesystem::exists(ffmpegPath)) {
            std::string cmd = ffmpegPath + " -f lavfi -i testsrc=duration=1:size=640x360:rate=30 -c:v libx264 -pix_fmt yuv420p -y test_source.mp4 > NUL 2>&1";
            std::system(cmd.c_str());
        }
    }

    static void TearDownTestSuite() {
        if (std::filesystem::exists("test_source.mp4")) {
            std::filesystem::remove("test_source.mp4");
        }
    }
};

TEST_F(FileSourceTest, OpenCloseAndStreams) {
    if (!std::filesystem::exists("test_source.mp4")) {
        GTEST_SKIP() << "test_source.mp4 not generated";
    }

    FileSource source;
    ASSERT_TRUE(source.Open("test_source.mp4").has_value());
    
    auto streams = source.GetStreams();
    EXPECT_FALSE(streams.empty());
    
    source.Close();
}

TEST_F(FileSourceTest, PullVideoFrames) {
    if (!std::filesystem::exists("test_source.mp4")) {
        GTEST_SKIP() << "test_source.mp4 not generated";
    }

    FileSource source;
    ASSERT_TRUE(source.Open("test_source.mp4").has_value());
    ASSERT_TRUE(source.Start().has_value());

    int frameCount = 0;
    while (true) {
        auto result = source.PullFrame();
        if (!result) {
            EXPECT_EQ(result.error().code, ErrorCode::EndOfStream);
            break;
        }
        EXPECT_NE(result.value(), nullptr);
        EXPECT_EQ(result.value()->GetMediaType(), MediaType::Video);
        frameCount++;
    }

    (void)source.Stop();
    source.Close();

    EXPECT_GE(frameCount, 25);
    EXPECT_LE(frameCount, 35);
}
