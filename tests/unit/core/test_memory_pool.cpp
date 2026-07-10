/// @file test_memory_pool.cpp
/// @brief Unit tests for MemoryPool

#include <openmedia/core/MemoryPool.h>
#include <gtest/gtest.h>

using namespace openmedia::core;

TEST(MemoryPoolTest, CreatePool) {
    MemoryPool pool(1024, 4);
    EXPECT_EQ(pool.GetTotalBlocks(), 4u);
    EXPECT_EQ(pool.GetAvailableBlocks(), 4u);
    EXPECT_EQ(pool.GetUsedBlocks(), 0u);
    EXPECT_EQ(pool.GetBlockSize(), 1024u);
    EXPECT_EQ(pool.GetTotalMemory(), 4096u);
}

TEST(MemoryPoolTest, AllocateAndRelease) {
    MemoryPool pool(256, 2);

    auto block1 = pool.Allocate();
    EXPECT_TRUE(block1.IsValid());
    EXPECT_EQ(pool.GetAvailableBlocks(), 1u);
    EXPECT_EQ(pool.GetUsedBlocks(), 1u);

    auto block2 = pool.Allocate();
    EXPECT_TRUE(block2.IsValid());
    EXPECT_EQ(pool.GetAvailableBlocks(), 0u);

    // Pool exhausted
    auto block3 = pool.Allocate();
    EXPECT_FALSE(block3.IsValid());

    // Release and re-allocate
    pool.Release(block1);
    EXPECT_EQ(pool.GetAvailableBlocks(), 1u);

    auto block4 = pool.Allocate();
    EXPECT_TRUE(block4.IsValid());
}

TEST(MemoryPoolTest, WriteAndReadData) {
    MemoryPool pool(64, 1);

    auto block = pool.Allocate();
    ASSERT_TRUE(block.IsValid());

    // Write data
    std::memset(block.data, 0xAB, block.size);

    // Verify
    EXPECT_EQ(block.data[0], 0xAB);
    EXPECT_EQ(block.data[63], 0xAB);
    EXPECT_EQ(block.size, 64u);

    pool.Release(block);
}
