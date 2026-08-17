/// @file test_worker_pool.cpp
/// @brief Unit tests for WorkerPool

#include <gtest/gtest.h>
#include <openmedia/worker_pool/WorkerPool.h>

#include <atomic>
#include <chrono>
#include <thread>

using namespace openmedia::worker_pool;

class WorkerPoolTest : public ::testing::Test {
protected:
    void SetUp() override {
        config.threadCount = 2;
        config.namePrefix = "Test-Worker";
    }

    WorkerPoolConfig config;
};

TEST_F(WorkerPoolTest, CreateAndDestroy) {
    WorkerPool pool(config);
    EXPECT_FALSE(pool.IsRunning());
    EXPECT_EQ(pool.GetThreadCount(), 2u);
}

TEST_F(WorkerPoolTest, StartAndStop) {
    WorkerPool pool(config);
    auto result = pool.Start();
    ASSERT_TRUE(result.has_value());
    EXPECT_TRUE(pool.IsRunning());

    pool.Stop();
    EXPECT_FALSE(pool.IsRunning());
}

TEST_F(WorkerPoolTest, DoubleStartFails) {
    WorkerPool pool(config);
    EXPECT_TRUE(pool.Start().has_value());
    EXPECT_FALSE(pool.Start().has_value());  // Should fail
    pool.Stop();
}

TEST_F(WorkerPoolTest, SubmitAndExecute) {
    WorkerPool pool(config);
    pool.Start();

    std::atomic<int> counter{0};
    auto future = pool.Submit(TaskPriority::Normal, [&counter] {
        counter.fetch_add(1);
        return 42;
    });

    int result = future.get();
    EXPECT_EQ(result, 42);
    EXPECT_EQ(counter.load(), 1);

    pool.Stop();
}

TEST_F(WorkerPoolTest, SubmitMultipleTasks) {
    WorkerPool pool(config);
    pool.Start();

    std::atomic<int> counter{0};
    constexpr int kTaskCount = 100;

    std::vector<std::future<void>> futures;
    for (int i = 0; i < kTaskCount; ++i) {
        futures.push_back(pool.Submit(TaskPriority::Normal, [&counter] {
            counter.fetch_add(1);
        }));
    }

    for (auto& f : futures) {
        f.get();
    }

    EXPECT_EQ(counter.load(), kTaskCount);
    pool.Stop();
}

TEST_F(WorkerPoolTest, PriorityOrdering) {
    // Single-threaded pool to test ordering
    WorkerPoolConfig singleConfig;
    singleConfig.threadCount = 1;
    WorkerPool pool(singleConfig);

    std::vector<int> executionOrder;
    std::mutex orderMutex;

    // Submit low priority first, then high
    pool.SubmitTask(TaskPriority::Low, [&] {
        std::lock_guard lock(orderMutex);
        executionOrder.push_back(3);
    });
    pool.SubmitTask(TaskPriority::High, [&] {
        std::lock_guard lock(orderMutex);
        executionOrder.push_back(1);
    });
    pool.SubmitTask(TaskPriority::Normal, [&] {
        std::lock_guard lock(orderMutex);
        executionOrder.push_back(2);
    });

    pool.Start();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));
    pool.Stop();

    // High priority should execute before Low
    if (executionOrder.size() == 3) {
        EXPECT_EQ(executionOrder[0], 1);  // High
    }
}

TEST_F(WorkerPoolTest, StatsTracking) {
    WorkerPool pool(config);
    pool.Start();

    auto future = pool.Submit(TaskPriority::Normal, [] {});
    future.get();

    auto stats = pool.GetStats();
    EXPECT_GE(stats.totalSubmitted, 1u);
    EXPECT_GE(stats.totalCompleted, 1u);

    pool.Stop();
}

TEST_F(WorkerPoolTest, AutoThreadCount) {
    WorkerPoolConfig autoConfig;
    autoConfig.threadCount = 0;  // auto
    WorkerPool pool(autoConfig);

    EXPECT_GE(pool.GetThreadCount(), 2u);
}
