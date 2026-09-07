/// @file test_seh_guard.cpp
/// @brief Unit tests for Windows SEH Crash Guard and fault isolation

#include <openmedia/core/SehGuard.h>
#include <openmedia/worker_pool/WorkerPool.h>
#include <gtest/gtest.h>

using namespace openmedia::core;
using namespace openmedia::worker_pool;

TEST(SehGuardTest, SuccessfulFunctionExecution) {
    bool executed = false;
    auto result = ExecuteWithSehGuard([&executed] {
        executed = true;
    }, "SuccessTest");

    EXPECT_TRUE(result.succeeded);
    EXPECT_TRUE(executed);
    EXPECT_EQ(result.exceptionCode, 0u);
}

TEST(SehGuardTest, StandardCppExceptionCaught) {
    auto result = ExecuteWithSehGuard([] {
        throw std::runtime_error("Deliberate failure");
    }, "ExceptionTest");

    EXPECT_FALSE(result.succeeded);
    EXPECT_NE(result.errorMessage.find("Deliberate failure"), std::string::npos);
}

#ifdef _WIN32
TEST(SehGuardTest, AccessViolationHardwareFaultIsolated) {
    auto result = ExecuteWithSehGuard([] {
        // Deliberately cause an Access Violation (0xC0000005)
        volatile int* nullPtr = nullptr;
        *nullPtr = 42;
    }, "AccessViolationTest");

    EXPECT_FALSE(result.succeeded);
    EXPECT_EQ(result.exceptionCode, static_cast<uint32_t>(EXCEPTION_ACCESS_VIOLATION));
    EXPECT_NE(result.errorMessage.find("0xC0000005"), std::string::npos);
}
#endif

TEST(SehGuardTest, WorkerPoolSurvivesTaskFault) {
    WorkerPoolConfig config;
    config.threadCount = 2;
    config.namePrefix = "SehTest";

    WorkerPool pool(config);
    ASSERT_TRUE(pool.Start().has_value());

    std::atomic<bool> secondTaskExecuted{false};

    // Task 1: Throws error / fault
    pool.Submit(TaskPriority::High, [] {
#ifdef _WIN32
        volatile int* nullPtr = nullptr;
        *nullPtr = 99;
#else
        throw std::runtime_error("Simulated crash");
#endif
    });

    // Task 2: Normal task submitted immediately after
    pool.Submit(TaskPriority::Normal, [&secondTaskExecuted] {
        secondTaskExecuted.store(true);
    });

    // Wait up to 1 second for worker to process Task 2
    for (int i = 0; i < 50; ++i) {
        if (secondTaskExecuted.load()) break;
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }

    // Thread pool survived and executed task 2!
    EXPECT_TRUE(secondTaskExecuted.load());
    EXPECT_TRUE(pool.IsRunning());

    pool.Stop();
}
