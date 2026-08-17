#include <gtest/gtest.h>
#include <openmedia/io/FileSource.h>
#include <openmedia/io/FileOutput.h>
#include <openmedia/codecs/CodecFactory.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/ErrorCodes.h>
#include <filesystem>
#include <thread>
#include <chrono>

using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::codecs;

class PipelineTranscodeTest : public ::testing::Test {
protected:
    void SetUp() override {
        inputFilePath = "test_transcode_input.h264";
        outputFilePath = "test_transcode_output.mp4";
        
        if (std::filesystem::exists(inputFilePath)) std::filesystem::remove(inputFilePath);
        if (std::filesystem::exists(outputFilePath)) std::filesystem::remove(outputFilePath);

        GenerateTestInputFile();
    }

    void TearDown() override {
        if (std::filesystem::exists(inputFilePath)) std::filesystem::remove(inputFilePath);
        if (std::filesystem::exists(outputFilePath)) std::filesystem::remove(outputFilePath);
    }

    std::string inputFilePath;
    std::string outputFilePath;

    void GenerateTestInputFile() {
        auto encoder = CodecFactory::CreateEncoder(VideoCodec::H264);
        ASSERT_TRUE(encoder != nullptr);
        ASSERT_TRUE(encoder->Initialize().has_value());

        auto out = std::make_shared<FileOutput>();
        out->AddVideoStream("libx264", 640, 480, 30, 1, 2000000);
        ASSERT_TRUE(out->Open(inputFilePath).has_value());


        ASSERT_TRUE(out->Start().has_value());
        ASSERT_TRUE(encoder->Start().has_value());

        for (int i = 0; i < 30; ++i) {
            auto frame = MediaFrame::CreateVideo(640, 480, PixelFormat::YUV420P);
            std::memset(frame->GetVideoPlane(0), 128 + i, 640 * 480);
            std::memset(frame->GetVideoPlane(1), 128, 320 * 240);
            std::memset(frame->GetVideoPlane(2), 128, 320 * 240);
            frame->SetPts(i);
            frame->SetTimeBase({1, 30});
            
            auto res = encoder->PushFrame(frame);
            ASSERT_TRUE(res.has_value());

            while (true) {
                auto pullRes = encoder->PullFrame();
                if (!pullRes || !pullRes.value()) break;
                auto pushRes = out->PushFrame(pullRes.value());
                ASSERT_TRUE(pushRes.has_value());
            }
        }

        (void)encoder->PushFrame(nullptr);
        while (true) {
            auto pullRes = encoder->PullFrame();
            if (!pullRes || !pullRes.value()) break;
            (void)out->PushFrame(pullRes.value());
        }

        (void)encoder->Stop();
        (void)out->Stop();
        out->Close();
        
        ASSERT_TRUE(std::filesystem::exists(inputFilePath));
    }
};

TEST_F(PipelineTranscodeTest, TranscodeH264) {
    auto fileSource = std::make_shared<FileSource>();
    ASSERT_TRUE(fileSource->Open(inputFilePath).has_value());

    auto decoder = CodecFactory::CreateDecoder(VideoCodec::H264);
    ASSERT_TRUE(decoder != nullptr);
    ASSERT_TRUE(decoder->Initialize().has_value());

    auto encoder = CodecFactory::CreateEncoder(VideoCodec::H264);
    ASSERT_TRUE(encoder != nullptr);
    ASSERT_TRUE(encoder->Initialize().has_value());

    auto fileOutput = std::make_shared<FileOutput>();
    fileOutput->AddVideoStream("libx264", 640, 480, 30, 1, 1000000);
    ASSERT_TRUE(fileOutput->Open(outputFilePath).has_value());


    ASSERT_TRUE(fileSource->Start().has_value());
    ASSERT_TRUE(decoder->Start().has_value());
    ASSERT_TRUE(encoder->Start().has_value());
    ASSERT_TRUE(fileOutput->Start().has_value());

    int frameCount = 0;

    while (true) {
        auto pullRes = fileSource->PullFrame();
        if (!pullRes || !pullRes.value()) break;

        auto pushDec = decoder->PushFrame(pullRes.value());
        ASSERT_TRUE(pushDec.has_value());

        while (true) {
            auto rawRes = decoder->PullFrame();
            if (!rawRes || !rawRes.value()) break;

            auto pushEnc = encoder->PushFrame(rawRes.value());
            ASSERT_TRUE(pushEnc.has_value());
            frameCount++;

            while (true) {
                auto encRes = encoder->PullFrame();
                if (!encRes || !encRes.value()) break;
                
                auto pushOut = fileOutput->PushFrame(encRes.value());
                ASSERT_TRUE(pushOut.has_value());
            }
        }
    }

    (void)decoder->PushFrame(nullptr);
    while (true) {
        auto rawRes = decoder->PullFrame();
        if (!rawRes || !rawRes.value()) break;

        (void)encoder->PushFrame(rawRes.value());
        frameCount++;

        while (true) {
            auto encRes = encoder->PullFrame();
            if (!encRes || !encRes.value()) break;
            (void)fileOutput->PushFrame(encRes.value());
        }
    }

    (void)encoder->PushFrame(nullptr);
    while (true) {
        auto encRes = encoder->PullFrame();
        if (!encRes || !encRes.value()) break;
        (void)fileOutput->PushFrame(encRes.value());
    }

    (void)fileSource->Stop();
    (void)decoder->Stop();
    (void)encoder->Stop();
    (void)fileOutput->Stop();
    
    fileSource->Close();
    fileOutput->Close();

    ASSERT_TRUE(std::filesystem::exists(outputFilePath));
    auto size = std::filesystem::file_size(outputFilePath);
    ASSERT_GT(size, 0);

    std::cout << "Transcoded " << frameCount << " frames successfully." << std::endl;
}
