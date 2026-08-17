#include <gtest/gtest.h>
#include <openmedia/io/FileOutput.h>
#include <openmedia/core/MediaFrame.h>
#include <fstream>
#include <filesystem>

using namespace openmedia::io;
using namespace openmedia::core;

TEST(FileOutputTest, AddStreamAndOpen) {
    FileOutput output;
    
    // Add H.264 stream
    auto ret = output.AddVideoStream("libx264", 1920, 1080, 60, 1, 5000000);
    ASSERT_TRUE(ret.has_value());

    // Open output file
    std::string testPath = "test_output.mp4";
    ret = output.Open(testPath);
    ASSERT_TRUE(ret.has_value());

    ASSERT_TRUE(output.Start().has_value());

    // Create a dummy encoded packet
    auto packetFrame = MediaFrame::CreatePacket(100);
    packetFrame->SetPts(0);
    packetFrame->SetDts(0);
    // Fill packet data with dummy NAL units or just zero data
    uint8_t* data = packetFrame->GetPacketData();
    std::fill(data, data + 100, 0);

    // Push frame
    ASSERT_TRUE(output.PushFrame(packetFrame).has_value());

    // Stop and close
    ASSERT_TRUE(output.Stop().has_value());
    output.Close();

    // Verify file exists and has size > 0
    ASSERT_TRUE(std::filesystem::exists(testPath));
    ASSERT_GT(std::filesystem::file_size(testPath), 0);

    // Cleanup
    std::filesystem::remove(testPath);
}
