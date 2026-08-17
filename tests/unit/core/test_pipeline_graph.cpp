#include <gtest/gtest.h>
#include <openmedia/core/PipelineGraph.h>
#include <openmedia/core/IMediaObject.h>

using namespace openmedia::core;

class MockMediaObject : public IMediaObject {
public:
    std::string GetName() const override { return "Mock"; }
    PipelineState GetState() const override { return started ? PipelineState::Running : PipelineState::Stopped; }
    VoidResult Initialize() override { initialized = true; return {}; }
    VoidResult Start() override { started = true; return {}; }
    VoidResult Stop() override { started = false; return {}; }
    VoidResult PushFrame(std::shared_ptr<MediaFrame> frame) override { return {}; }
    Result<std::shared_ptr<MediaFrame>> PullFrame() override { return std::unexpected(OME_ERROR(ErrorCode::NotSupported, "")); }
    VoidResult Connect(std::shared_ptr<IMediaObject>) override { return {}; }
    VoidResult Disconnect() override { return {}; }
    void OnStateChange(StateChangeCallback) override {}
    void OnError(ErrorCallback) override {}

    bool initialized = false;
    bool started = false;
};

TEST(PipelineGraphTest, AddRemoveNodes) {
    PipelineGraph graph("test");

    auto n1 = graph.AddNode("source", NodeType::Source);
    auto n2 = graph.AddNode("output", NodeType::Output);

    EXPECT_EQ(graph.GetNodeCount(), 2);
    EXPECT_EQ(graph.GetNode(n1)->name, "source");
    EXPECT_EQ(graph.GetNode(n2)->name, "output");

    auto res = graph.RemoveNode(n1);
    EXPECT_TRUE(res.has_value());
    EXPECT_EQ(graph.GetNodeCount(), 1);
    EXPECT_EQ(graph.GetNode(n1), nullptr);
}

TEST(PipelineGraphTest, ConnectNodes) {
    PipelineGraph graph("test");
    auto n1 = graph.AddNode("source", NodeType::Source);
    auto n2 = graph.AddNode("output", NodeType::Output);

    auto res = graph.Connect(n1, n2);
    EXPECT_TRUE(res.has_value());

    auto edges = graph.GetEdges();
    ASSERT_EQ(edges.size(), 1);
    EXPECT_EQ(edges[0].source, n1);
    EXPECT_EQ(edges[0].target, n2);

    auto disRes = graph.Disconnect(n1, n2);
    EXPECT_TRUE(disRes.has_value());
    EXPECT_EQ(graph.GetEdges().size(), 0);
}

TEST(PipelineGraphTest, ValidateGraph) {
    PipelineGraph graph("test");
    
    // Empty graph
    auto val = graph.Validate();
    EXPECT_TRUE(val.valid);

    // No output
    auto n1 = graph.AddNode("source", NodeType::Source);
    val = graph.Validate();
    EXPECT_TRUE(val.valid); // Warnings but valid
    EXPECT_FALSE(val.warnings.empty());

    // Connect
    auto n2 = graph.AddNode("output", NodeType::Output);
    std::ignore = graph.Connect(n1, n2);
    val = graph.Validate();
    EXPECT_TRUE(val.valid);
}

TEST(PipelineGraphTest, CycleDetection) {
    PipelineGraph graph("test");
    auto n1 = graph.AddNode("source", NodeType::Source);
    auto n2 = graph.AddNode("filter", NodeType::Filter);
    auto n3 = graph.AddNode("output", NodeType::Output);

    std::ignore = graph.Connect(n1, n2);
    std::ignore = graph.Connect(n2, n3);
    EXPECT_FALSE(graph.HasCycles());

    std::ignore = graph.Connect(n3, n1); // Create a cycle
    EXPECT_TRUE(graph.HasCycles());
}

TEST(PipelineGraphTest, ExecutionOrder) {
    PipelineGraph graph("test");
    auto n1 = graph.AddNode("source", NodeType::Source);
    auto n2 = graph.AddNode("filter", NodeType::Filter);
    auto n3 = graph.AddNode("output", NodeType::Output);

    std::ignore = graph.Connect(n1, n2);
    std::ignore = graph.Connect(n2, n3);

    auto order = graph.GetExecutionOrder();
    ASSERT_TRUE(order.has_value());
    ASSERT_EQ(order->size(), 3);
    EXPECT_EQ(order->at(0), n1);
    EXPECT_EQ(order->at(1), n2);
    EXPECT_EQ(order->at(2), n3);
}

TEST(PipelineGraphTest, Lifecycle) {
    PipelineGraph graph("test");
    
    auto obj1 = std::make_shared<MockMediaObject>();
    auto obj2 = std::make_shared<MockMediaObject>();

    auto n1 = graph.AddNode("source", NodeType::Source, obj1);
    auto n2 = graph.AddNode("output", NodeType::Output, obj2);
    std::ignore = graph.Connect(n1, n2);

    EXPECT_TRUE(graph.Build().has_value());
    EXPECT_EQ(graph.GetState(), PipelineState::Ready);

    EXPECT_TRUE(graph.Start().has_value());
    EXPECT_EQ(graph.GetState(), PipelineState::Running);

    EXPECT_TRUE(obj1->initialized);
    EXPECT_TRUE(obj1->started);
    EXPECT_TRUE(obj2->initialized);
    EXPECT_TRUE(obj2->started);

    EXPECT_TRUE(graph.Pause().has_value());
    EXPECT_EQ(graph.GetState(), PipelineState::Paused);

    EXPECT_TRUE(graph.Resume().has_value());
    EXPECT_EQ(graph.GetState(), PipelineState::Running);

    EXPECT_TRUE(graph.Stop().has_value());
    EXPECT_EQ(graph.GetState(), PipelineState::Stopped);

    EXPECT_FALSE(obj1->started);
    EXPECT_FALSE(obj2->started);
}

TEST(PipelineGraphTest, JsonSerialization) {
    PipelineGraph graph("my_graph");
    auto n1 = graph.AddNode("source", NodeType::Source);
    auto n2 = graph.AddNode("mixer", NodeType::Mixer);
    auto n3 = graph.AddNode("output", NodeType::Output);

    std::ignore = graph.Connect(n1, n2, 0, 0);
    std::ignore = graph.Connect(n2, n3, 0, 0);

    auto jsonRes = graph.ToJson();
    ASSERT_TRUE(jsonRes.has_value());
    std::string json = *jsonRes;

    auto deserializedRes = PipelineGraph::FromJson(json);
    ASSERT_TRUE(deserializedRes.has_value());
    
    auto& deserialized = *deserializedRes;
    EXPECT_EQ(deserialized.GetName(), "my_graph");
    EXPECT_EQ(deserialized.GetNodeCount(), 3);
    EXPECT_EQ(deserialized.GetEdges().size(), 2);
}
