/// @file test_shared_memory.cpp
/// @brief Unit tests for SharedMemoryBuffer

#include <gtest/gtest.h>
#include <openmedia/ipc/SharedMemoryBuffer.h>

#include <cstring>
#include <random>
#include <string>

using namespace openmedia::ipc;

class SharedMemoryTest : public ::testing::Test {
protected:
    void SetUp() override {
        // Use unique name per test to avoid collisions
        static int counter = 0;
        config.name = "OME_Test_SharedMem_" + std::to_string(++counter);
        config.frameSlotCount = 4;
        config.maxFrameSize = 1920 * 1080 * 4;  // BGRA 1080p
    }

    void TearDown() override {
        // Cleanup happens in destructors
    }

    SharedMemoryConfig config;
};

TEST_F(SharedMemoryTest, CreateAndDestroy) {
    SharedMemoryBuffer buffer(config, true);
    EXPECT_FALSE(buffer.IsInitialized());

    auto result = buffer.Initialize();
    ASSERT_TRUE(result.has_value()) << result.error().message;
    EXPECT_TRUE(buffer.IsInitialized());
    EXPECT_EQ(buffer.GetSlotCount(), 4u);
    EXPECT_EQ(buffer.GetMaxFrameSize(), 1920u * 1080u * 4u);
    EXPECT_GT(buffer.GetTotalSize(), 0u);

    buffer.Close();
    EXPECT_FALSE(buffer.IsInitialized());
}

TEST_F(SharedMemoryTest, WriteAndReadSlot) {
    SharedMemoryBuffer producer(config, true);
    auto result = producer.Initialize();
    ASSERT_TRUE(result.has_value()) << result.error().message;

    // Write a frame
    uint32_t writeSlot;
    uint8_t* writePtr = producer.AcquireWriteSlot(writeSlot);
    ASSERT_NE(writePtr, nullptr);

    // Fill with test pattern
    constexpr uint32_t testSize = 128;
    for (uint32_t i = 0; i < testSize; ++i) {
        writePtr[i] = static_cast<uint8_t>(i & 0xFF);
    }

    FrameSlotHeader metadata;
    metadata.pts = 1000;
    metadata.width = 1920;
    metadata.height = 1080;
    metadata.dataSize = testSize;
    metadata.sequenceNumber = 1;
    producer.CommitWriteSlot(writeSlot, metadata);

    // Read the frame (same process for testing)
    // In real usage, a different process opens the same shared memory
    uint32_t readSlot;
    FrameSlotHeader readMetadata;
    const uint8_t* readPtr = producer.AcquireReadSlot(readSlot, readMetadata);
    ASSERT_NE(readPtr, nullptr);

    EXPECT_EQ(readMetadata.pts, 1000u);
    EXPECT_EQ(readMetadata.width, 1920u);
    EXPECT_EQ(readMetadata.height, 1080u);
    EXPECT_EQ(readMetadata.dataSize, testSize);
    EXPECT_EQ(readMetadata.sequenceNumber, 1u);

    // Verify data
    for (uint32_t i = 0; i < testSize; ++i) {
        EXPECT_EQ(readPtr[i], static_cast<uint8_t>(i & 0xFF));
    }

    producer.ReleaseReadSlot(readSlot);
}

TEST_F(SharedMemoryTest, MultipleFrameRingBuffer) {
    SharedMemoryBuffer buffer(config, true);
    buffer.Initialize();

    // Write multiple frames
    for (uint32_t seq = 0; seq < config.frameSlotCount; ++seq) {
        uint32_t slot;
        uint8_t* ptr = buffer.AcquireWriteSlot(slot);
        ASSERT_NE(ptr, nullptr) << "Failed at seq " << seq;

        ptr[0] = static_cast<uint8_t>(seq);

        FrameSlotHeader meta;
        meta.sequenceNumber = seq;
        meta.dataSize = 1;
        buffer.CommitWriteSlot(slot, meta);
    }

    // Read all frames in order
    for (uint32_t seq = 0; seq < config.frameSlotCount; ++seq) {
        uint32_t slot;
        FrameSlotHeader meta;
        const uint8_t* ptr = buffer.AcquireReadSlot(slot, meta);
        ASSERT_NE(ptr, nullptr) << "Failed to read seq " << seq;
        EXPECT_EQ(ptr[0], static_cast<uint8_t>(seq));
        EXPECT_EQ(meta.sequenceNumber, seq);
        buffer.ReleaseReadSlot(slot);
    }
}

TEST_F(SharedMemoryTest, EmptyReadReturnsNull) {
    SharedMemoryBuffer buffer(config, true);
    buffer.Initialize();

    uint32_t slot;
    FrameSlotHeader meta;
    const uint8_t* ptr = buffer.AcquireReadSlot(slot, meta);
    EXPECT_EQ(ptr, nullptr);
}

TEST_F(SharedMemoryTest, PositionTracking) {
    SharedMemoryBuffer buffer(config, true);
    buffer.Initialize();

    EXPECT_EQ(buffer.GetWritePosition(), 0u);
    EXPECT_EQ(buffer.GetReadPosition(), 0u);

    // Write a frame
    uint32_t slot;
    uint8_t* ptr = buffer.AcquireWriteSlot(slot);
    FrameSlotHeader meta{};
    meta.dataSize = 1;
    buffer.CommitWriteSlot(slot, meta);

    EXPECT_EQ(buffer.GetWritePosition(), 1u);
    EXPECT_EQ(buffer.GetReadPosition(), 0u);

    // Read the frame
    FrameSlotHeader readMeta;
    buffer.AcquireReadSlot(slot, readMeta);
    buffer.ReleaseReadSlot(slot);

    EXPECT_EQ(buffer.GetReadPosition(), 1u);
}

TEST_F(SharedMemoryTest, GetName) {
    SharedMemoryBuffer buffer(config, true);
    EXPECT_EQ(buffer.GetName(), config.name);
}
