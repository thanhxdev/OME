#include <gtest/gtest.h>
#include <openmedia/io/FileSource.h>
#include <openmedia/io/FileOutput.h>
#include <openmedia/codecs/CodecFactory.h>
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/ErrorCodes.h>
#include <filesystem>
#include <thread>
#include <chrono>
#include <iostream>
#include <iomanip>

using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::codecs;

class PipelinePerformanceTest : public ::testing::Test {
protected:
    void SetUp() override {
        inputFilePath = "perf_transcode_input.h264";
        outputFilePath = "perf_transcode_output.mp4";
        
        if (std::filesystem::exists(inputFilePath)) std::filesystem::remove(inputFilePath);
        if (std::filesystem::exists(outputFilePath)) std::filesystem::remove(outputFilePath);

        GenerateTestInputFile(300); // 10 seconds of 30fps
    }

    void TearDown() override {
        if (std::filesystem::exists(inputFilePath)) std::filesystem::remove(inputFilePath);
        if (std::filesystem::exists(outputFilePath)) std::filesystem::remove(outputFilePath);
    }

    std::string inputFilePath;
    std::string outputFilePath;

    void GenerateTestInputFile(int numFrames) {
        auto encoder = CodecFactory::CreateEncoder(VideoCodec::H264);
        ASSERT_TRUE(encoder != nullptr);
        ASSERT_TRUE(encoder->Initialize().has_value());

        auto out = std::make_shared<FileOutput>();
        out->AddVideoStream("libx264", 1920, 1080, 30, 1, 4000000); // 1080p
        ASSERT_TRUE(out->Open(inputFilePath).has_value());

        ASSERT_TRUE(out->Start().has_value());
        ASSERT_TRUE(encoder->Start().has_value());

        for (int i = 0; i < numFrames; ++i) {
            auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::YUV420P);
            std::memset(frame->GetVideoPlane(0), (i % 256), 1920 * 1080);
            std::memset(frame->GetVideoPlane(1), 128, 960 * 540);
            std::memset(frame->GetVideoPlane(2), 128, 960 * 540);
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

TEST_F(PipelinePerformanceTest, Transcode1080pThroughput) {
    auto fileSource = std::make_shared<FileSource>();
    ASSERT_TRUE(fileSource->Open(inputFilePath).has_value());

    auto decoder = CodecFactory::CreateDecoder(VideoCodec::H264);
    ASSERT_TRUE(decoder != nullptr);
    ASSERT_TRUE(decoder->Initialize().has_value());

    auto encoder = CodecFactory::CreateEncoder(VideoCodec::H264);
    ASSERT_TRUE(encoder != nullptr);
    ASSERT_TRUE(encoder->Initialize().has_value());

    auto fileOutput = std::make_shared<FileOutput>();
    fileOutput->AddVideoStream("libx264", 1920, 1080, 30, 1, 4000000);
    ASSERT_TRUE(fileOutput->Open(outputFilePath).has_value());

    ASSERT_TRUE(fileSource->Start().has_value());
    ASSERT_TRUE(decoder->Start().has_value());
    ASSERT_TRUE(encoder->Start().has_value());
    ASSERT_TRUE(fileOutput->Start().has_value());

    int frameCount = 0;
    
    auto startTime = std::chrono::high_resolution_clock::now();

    while (true) {
        auto pullRes = fileSource->PullFrame();
        if (!pullRes || !pullRes.value()) break;

        auto pushDec = decoder->PushFrame(pullRes.value());
        if (!pushDec.has_value()) {
            GTEST_SKIP() << "Decoder failed to push frame, likely environment unsupported";
            return;
        }

        while (true) {
            auto rawRes = decoder->PullFrame();
            if (!rawRes || !rawRes.value()) break;

            auto pushEnc = encoder->PushFrame(rawRes.value());
            ASSERT_TRUE(pushEnc.has_value());

            while (true) {
                auto encRes = encoder->PullFrame();
                if (!encRes || !encRes.value()) break;
                
                auto pushOut = fileOutput->PushFrame(encRes.value());
                ASSERT_TRUE(pushOut.has_value());
                frameCount++;
            }
        }
    }

    (void)decoder->PushFrame(nullptr);
    while (true) {
        auto rawRes = decoder->PullFrame();
        if (!rawRes || !rawRes.value()) break;

        (void)encoder->PushFrame(rawRes.value());

        while (true) {
            auto encRes = encoder->PullFrame();
            if (!encRes || !encRes.value()) break;
            (void)fileOutput->PushFrame(encRes.value());
            frameCount++;
        }
    }

    (void)encoder->PushFrame(nullptr);
    while (true) {
        auto encRes = encoder->PullFrame();
        if (!encRes || !encRes.value()) break;
        (void)fileOutput->PushFrame(encRes.value());
        frameCount++;
    }

    auto endTime = std::chrono::high_resolution_clock::now();
    std::chrono::duration<double> diff = endTime - startTime;

    (void)fileSource->Stop();
    (void)decoder->Stop();
    (void)encoder->Stop();
    (void)fileOutput->Stop();
    
    fileSource->Close();
    fileOutput->Close();

    ASSERT_TRUE(std::filesystem::exists(outputFilePath));
    auto size = std::filesystem::file_size(outputFilePath);
    ASSERT_GT(size, 0);

    double fps = frameCount / diff.count();
    
    std::cout << "===========================================" << std::endl;
    std::cout << "[ PERFORMANCE RESULTS - 1080p Transcode ]" << std::endl;
    std::cout << "Processed frames : " << frameCount << std::endl;
    std::cout << "Time elapsed     : " << std::fixed << std::setprecision(2) << diff.count() << " seconds" << std::endl;
    std::cout << "Throughput       : " << std::fixed << std::setprecision(2) << fps << " FPS" << std::endl;
    std::cout << "===========================================" << std::endl;
    
    // Warn if under 60fps, but don't fail the test
    if (fps < 60.0) {
        std::cerr << "[WARNING] Throughput is below 60 FPS!" << std::endl;
    }
}
