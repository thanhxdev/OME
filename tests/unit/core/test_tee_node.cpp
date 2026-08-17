#include <gtest/gtest.h>
#include <openmedia/core/TeeNode.h>
#include <openmedia/core/MediaFrame.h>

using namespace openmedia::core;

class MockDownstream : public IMediaObject {
public:
    std::string GetName() const override { return "MockDownstream"; }
    PipelineState GetState() const override { return PipelineState::Running; }

    VoidResult Initialize() override { return {}; }
    VoidResult Start() override { return {}; }
    VoidResult Stop() override { return {}; }

    VoidResult PushFrame(std::shared_ptr<MediaFrame> frame) override {
        pushedFrames.push_back(frame);
        return {};
    }

    Result<std::shared_ptr<MediaFrame>> PullFrame() override {
        return std::unexpected(Error::Make(ErrorCode::NotImplemented, "Not implemented"));
    }

    VoidResult Connect(std::shared_ptr<IMediaObject> downstream) override { return {}; }
    VoidResult Disconnect() override { return {}; }

    void OnStateChange(StateChangeCallback callback) override {}
    void OnError(ErrorCallback callback) override {}

    std::vector<std::shared_ptr<MediaFrame>> pushedFrames;
};

class TeeNodeTest : public ::testing::Test {
protected:
    void SetUp() override {
        teeNode = std::make_shared<TeeNode>();
    }

    std::shared_ptr<TeeNode> teeNode;
};

TEST_F(TeeNodeTest, FanOutToMultipleDownstreams) {
    auto dest1 = std::make_shared<MockDownstream>();
    auto dest2 = std::make_shared<MockDownstream>();
    auto dest3 = std::make_shared<MockDownstream>();

    EXPECT_TRUE(teeNode->Connect(dest1).has_value());
    EXPECT_TRUE(teeNode->Connect(dest2).has_value());
    EXPECT_TRUE(teeNode->Connect(dest3).has_value());

    teeNode->Initialize();
    teeNode->Start();

    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);

    EXPECT_TRUE(teeNode->PushFrame(frame).has_value());

    // Verify all downstreams received the exact same frame (shared_ptr)
    ASSERT_EQ(dest1->pushedFrames.size(), 1);
    EXPECT_EQ(dest1->pushedFrames[0], frame);

    ASSERT_EQ(dest2->pushedFrames.size(), 1);
    EXPECT_EQ(dest2->pushedFrames[0], frame);

    ASSERT_EQ(dest3->pushedFrames.size(), 1);
    EXPECT_EQ(dest3->pushedFrames[0], frame);
}

TEST_F(TeeNodeTest, DisconnectRemovesDownstream) {
    auto dest1 = std::make_shared<MockDownstream>();
    auto dest2 = std::make_shared<MockDownstream>();

    teeNode->Connect(dest1);
    teeNode->Connect(dest2);
    
    teeNode->Initialize();
    teeNode->Start();
    
    teeNode->Disconnect(dest1);
    
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    teeNode->PushFrame(frame);
    
    // dest1 should have 0 frames, dest2 should have 1
    EXPECT_EQ(dest1->pushedFrames.size(), 0);
    EXPECT_EQ(dest2->pushedFrames.size(), 1);
}

TEST_F(TeeNodeTest, CannotPushWhenStopped) {
    auto dest = std::make_shared<MockDownstream>();
    teeNode->Connect(dest);
    
    auto frame = MediaFrame::CreateVideo(1920, 1080, PixelFormat::NV12);
    
    // Default state is Stopped, should fail
    auto res = teeNode->PushFrame(frame);
    EXPECT_FALSE(res.has_value());
    EXPECT_EQ(static_cast<int>(res.error().code), static_cast<int>(ErrorCode::InvalidState));
    
    EXPECT_EQ(dest->pushedFrames.size(), 0);
}

