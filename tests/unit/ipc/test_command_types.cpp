/// @file test_command_types.cpp
/// @brief Unit tests for IPC command types and message headers

#include <gtest/gtest.h>
#include <openmedia/ipc/CommandTypes.h>

using namespace openmedia::ipc;

TEST(CommandTypes, MessageHeaderDefaults) {
    MessageHeader header;
    EXPECT_EQ(header.magic, MessageHeader::MAGIC);
    EXPECT_EQ(header.version, MessageHeader::VERSION);
    EXPECT_EQ(header.sequenceNumber, 0u);
    EXPECT_EQ(header.payloadSize, 0u);
    EXPECT_TRUE(header.IsValid());
}

TEST(CommandTypes, MessageHeaderMagicValidation) {
    MessageHeader header;
    EXPECT_TRUE(header.IsValid());

    header.magic = 0xDEADBEEF;
    EXPECT_FALSE(header.IsValid());
}

TEST(CommandTypes, ResponseHeaderDefaults) {
    ResponseHeader header;
    EXPECT_EQ(header.status, ResponseStatus::Success);
    EXPECT_EQ(header.errorCode, 0u);
    EXPECT_TRUE(header.IsValid());
}

TEST(CommandTypes, ResponseHeaderInvalid) {
    ResponseHeader header;
    header.version = 99;
    EXPECT_FALSE(header.IsValid());
}

TEST(CommandTypes, CommandTypeToStringKnown) {
    EXPECT_EQ(CommandTypeToString(CommandType::Noop), "Noop");
    EXPECT_EQ(CommandTypeToString(CommandType::Heartbeat), "Heartbeat");
    EXPECT_EQ(CommandTypeToString(CommandType::CreatePipeline), "CreatePipeline");
    EXPECT_EQ(CommandTypeToString(CommandType::Shutdown), "Shutdown");
    EXPECT_EQ(CommandTypeToString(CommandType::OpenSource), "OpenSource");
    EXPECT_EQ(CommandTypeToString(CommandType::AddMixerInput), "AddMixerInput");
    EXPECT_EQ(CommandTypeToString(CommandType::LoadPlugin), "LoadPlugin");
    EXPECT_EQ(CommandTypeToString(CommandType::RequestFrame), "RequestFrame");
}

TEST(CommandTypes, CommandTypeToStringUnknown) {
    EXPECT_EQ(CommandTypeToString(static_cast<CommandType>(0xFFFF)), "Unknown");
}

TEST(CommandTypes, CommandTypeValues) {
    // Verify enum categories are in correct ranges
    EXPECT_LT(static_cast<uint32_t>(CommandType::Shutdown), 0x0100u);
    EXPECT_GE(static_cast<uint32_t>(CommandType::CreatePipeline), 0x0100u);
    EXPECT_LT(static_cast<uint32_t>(CommandType::CreatePipeline), 0x0200u);
    EXPECT_GE(static_cast<uint32_t>(CommandType::OpenSource), 0x0200u);
    EXPECT_LT(static_cast<uint32_t>(CommandType::OpenSource), 0x0300u);
    EXPECT_GE(static_cast<uint32_t>(CommandType::AddMixerInput), 0x0300u);
}

TEST(CommandTypes, HeaderSize) {
    // Ensure headers have consistent binary layout
    EXPECT_GE(sizeof(MessageHeader), 28u);
    EXPECT_GE(sizeof(ResponseHeader), 24u);
}
