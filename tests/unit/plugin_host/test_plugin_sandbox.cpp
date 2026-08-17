#include <gtest/gtest.h>
#include <openmedia/plugin_host/PluginSandbox.h>
#include <stdexcept>
#include <thread>
#include <chrono>

using namespace openmedia::plugin_host;

TEST(PluginSandboxTest, SuccessCase) {
    SandboxConfig config;
    PluginSandbox sandbox(config);

    int result = 0;
    auto report = sandbox.Execute("TestPlugin", [&]() {
        result = 42;
    });

    EXPECT_EQ(report.result, SandboxResult::Success);
    EXPECT_EQ(result, 42);
    EXPECT_EQ(sandbox.GetCaughtExceptionCount(), 0);
    EXPECT_EQ(sandbox.GetTimeoutCount(), 0);
}

TEST(PluginSandboxTest, CxxException) {
    SandboxConfig config;
    PluginSandbox sandbox(config);

    auto report = sandbox.Execute("TestPlugin", [&]() {
        throw std::runtime_error("simulated crash");
    });

    EXPECT_TRUE(report.result == SandboxResult::Exception || report.result == SandboxResult::SEHException);
    EXPECT_FALSE(report.errorMessage.empty());
    EXPECT_EQ(sandbox.GetCaughtExceptionCount(), 1);
}

TEST(PluginSandboxTest, Timeout) {
    SandboxConfig config;
    config.defaultTimeout = std::chrono::milliseconds(50);
    PluginSandbox sandbox(config);

    auto report = sandbox.Execute("TestPlugin", [&]() {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    });

    EXPECT_EQ(report.result, SandboxResult::Timeout);
    EXPECT_EQ(sandbox.GetTimeoutCount(), 1);
}

TEST(PluginSandboxTest, ExecuteWithResult) {
    SandboxConfig config;
    PluginSandbox sandbox(config);

    auto result = sandbox.ExecuteWithResult<int>("TestPlugin", [&]() {
        return 99;
    });

    ASSERT_TRUE(result.has_value());
    EXPECT_EQ(result.value(), 99);
}

TEST(PluginSandboxTest, ExecuteWithResultException) {
    SandboxConfig config;
    PluginSandbox sandbox(config);

    auto result = sandbox.ExecuteWithResult<int>("TestPlugin", [&]() -> int {
        throw std::logic_error("bad logic");
    });

    ASSERT_FALSE(result.has_value());
    EXPECT_EQ(result.error().code, openmedia::core::ErrorCode::PluginCrashed);
}
