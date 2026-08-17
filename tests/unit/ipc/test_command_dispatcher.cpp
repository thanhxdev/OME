/// @file test_command_dispatcher.cpp
/// @brief Unit tests for CommandDispatcher

#include <gtest/gtest.h>
#include <openmedia/command_dispatcher/CommandDispatcher.h>

using namespace openmedia;
using namespace openmedia::command_dispatcher;

class CommandDispatcherTest : public ::testing::Test {
protected:
    CommandDispatcher dispatcher;
};

TEST_F(CommandDispatcherTest, RegisterAndHasHandler) {
    EXPECT_FALSE(dispatcher.HasHandler(ipc::CommandType::GetStatus));
    EXPECT_EQ(dispatcher.GetHandlerCount(), 0u);

    dispatcher.Register(ipc::CommandType::GetStatus,
        [](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            return std::vector<uint8_t>{0x01};
        });

    EXPECT_TRUE(dispatcher.HasHandler(ipc::CommandType::GetStatus));
    EXPECT_EQ(dispatcher.GetHandlerCount(), 1u);
}

TEST_F(CommandDispatcherTest, Unregister) {
    dispatcher.Register(ipc::CommandType::GetStatus,
        [](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            return std::vector<uint8_t>{};
        });

    EXPECT_TRUE(dispatcher.HasHandler(ipc::CommandType::GetStatus));
    dispatcher.Unregister(ipc::CommandType::GetStatus);
    EXPECT_FALSE(dispatcher.HasHandler(ipc::CommandType::GetStatus));
}

TEST_F(CommandDispatcherTest, DispatchSuccess) {
    dispatcher.Register(ipc::CommandType::Heartbeat,
        [](uint32_t clientId, const std::vector<uint8_t>& /*payload*/)
            -> core::Result<std::vector<uint8_t>> {
            EXPECT_EQ(clientId, 42u);
            return std::vector<uint8_t>{0xAA, 0xBB};
        });

    auto result = dispatcher.Dispatch(42, ipc::CommandType::Heartbeat, {});
    ASSERT_TRUE(result.has_value());
    EXPECT_EQ(result->size(), 2u);
    EXPECT_EQ((*result)[0], 0xAA);
    EXPECT_EQ((*result)[1], 0xBB);
}

TEST_F(CommandDispatcherTest, DispatchNotFound) {
    auto result = dispatcher.Dispatch(1, ipc::CommandType::Shutdown, {});
    EXPECT_FALSE(result.has_value());
    EXPECT_EQ(result.error().code, core::ErrorCode::NotFound);
}

TEST_F(CommandDispatcherTest, DispatchPassesPayload) {
    dispatcher.Register(ipc::CommandType::SetConfig,
        [](uint32_t, const std::vector<uint8_t>& payload)
            -> core::Result<std::vector<uint8_t>> {
            EXPECT_EQ(payload.size(), 3u);
            EXPECT_EQ(payload[0], 1);
            EXPECT_EQ(payload[1], 2);
            EXPECT_EQ(payload[2], 3);
            return std::vector<uint8_t>{};
        });

    auto result = dispatcher.Dispatch(1, ipc::CommandType::SetConfig, {1, 2, 3});
    EXPECT_TRUE(result.has_value());
}

TEST_F(CommandDispatcherTest, StatsTracking) {
    dispatcher.Register(ipc::CommandType::Heartbeat,
        [](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            return std::vector<uint8_t>{};
        });

    std::ignore = dispatcher.Dispatch(1, ipc::CommandType::Heartbeat, {});
    std::ignore = dispatcher.Dispatch(1, ipc::CommandType::Heartbeat, {});
    std::ignore = dispatcher.Dispatch(1, ipc::CommandType::Shutdown, {}); // no handler

    auto stats = dispatcher.GetStats();
    EXPECT_EQ(stats.totalDispatched, 3u);
    EXPECT_EQ(stats.totalSuccess, 2u);
    EXPECT_EQ(stats.totalErrors, 1u);
}

TEST_F(CommandDispatcherTest, MultipleHandlers) {
    int handler1Called = 0;
    int handler2Called = 0;

    dispatcher.Register(ipc::CommandType::GetStatus,
        [&](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            handler1Called++;
            return std::vector<uint8_t>{};
        });

    dispatcher.Register(ipc::CommandType::Heartbeat,
        [&](uint32_t, const std::vector<uint8_t>&)
            -> core::Result<std::vector<uint8_t>> {
            handler2Called++;
            return std::vector<uint8_t>{};
        });

    std::ignore = dispatcher.Dispatch(1, ipc::CommandType::GetStatus, {});
    std::ignore = dispatcher.Dispatch(1, ipc::CommandType::Heartbeat, {});
    std::ignore = dispatcher.Dispatch(1, ipc::CommandType::GetStatus, {});

    EXPECT_EQ(handler1Called, 2);
    EXPECT_EQ(handler2Called, 1);
}
