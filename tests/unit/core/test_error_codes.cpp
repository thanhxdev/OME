/// @file test_error_codes.cpp
/// @brief Unit tests for error handling types

#include <openmedia/core/ErrorCodes.h>
#include <gtest/gtest.h>

using namespace openmedia::core;

TEST(ErrorCodesTest, ErrorSuccess) {
    auto err = Error::Ok();
    EXPECT_TRUE(err.IsSuccess());
    EXPECT_FALSE(err.IsError());
    EXPECT_EQ(err.code, ErrorCode::Success);
}

TEST(ErrorCodesTest, ErrorMake) {
    auto err = Error::Make(ErrorCode::FileNotFound, "test.mp4 not found", "test.cpp", 42);
    EXPECT_TRUE(err.IsError());
    EXPECT_FALSE(err.IsSuccess());
    EXPECT_EQ(err.code, ErrorCode::FileNotFound);
    EXPECT_EQ(err.message, "test.mp4 not found");
    EXPECT_EQ(err.source, "test.cpp");
    EXPECT_EQ(err.line, 42);
}

TEST(ErrorCodesTest, ErrorCodeToString) {
    EXPECT_EQ(ErrorCodeToString(ErrorCode::Success), "Success");
    EXPECT_EQ(ErrorCodeToString(ErrorCode::FileNotFound), "File not found");
    EXPECT_EQ(ErrorCodeToString(ErrorCode::GPUNotAvailable), "GPU not available");
}

TEST(ErrorCodesTest, ResultSuccess) {
    Result<int> result = 42;
    EXPECT_TRUE(result.has_value());
    EXPECT_EQ(result.value(), 42);
}

TEST(ErrorCodesTest, ResultError) {
    Result<int> result = std::unexpected(
        Error::Make(ErrorCode::InvalidArgument, "bad value"));
    EXPECT_FALSE(result.has_value());
    EXPECT_EQ(result.error().code, ErrorCode::InvalidArgument);
}

TEST(ErrorCodesTest, VoidResultSuccess) {
    VoidResult result;
    EXPECT_TRUE(result.has_value());
}

TEST(ErrorCodesTest, VoidResultError) {
    VoidResult result = std::unexpected(
        Error::Make(ErrorCode::PipelineBuildFailed, "no source"));
    EXPECT_FALSE(result.has_value());
}
