#pragma once

/// @file PipelineGraph.h
/// @brief DAG-based pipeline graph with fan-in/fan-out support
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>
#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/Types.h>

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace openmedia::core {

/// @brief Unique identifier for a graph node
using NodeId = uint32_t;

/// @brief Node type in the pipeline graph
enum class NodeType : uint32_t {
    Source = 0,
    Filter,
    Mixer,
    Encoder,
    Decoder,
    Output,
    Tee,        ///< Fan-out node (1 input → N outputs)
};

/// @brief A single node in the pipeline DAG
struct GraphNode {
    NodeId id = 0;
    std::string name;
    NodeType type = NodeType::Filter;
    std::shared_ptr<IMediaObject> mediaObject;

    /// @brief Input connections (nodes that feed into this node)
    std::vector<NodeId> inputs;

    /// @brief Output connections (nodes this node feeds into)
    std::vector<NodeId> outputs;
};

/// @brief Edge between two nodes
struct GraphEdge {
    NodeId source = 0;      ///< From node
    NodeId target = 0;      ///< To node
    uint32_t sourcePin = 0; ///< Output pin index on source
    uint32_t targetPin = 0; ///< Input pin index on target
};

/// @brief Graph validation result
struct GraphValidation {
    bool valid = false;
    std::vector<std::string> errors;
    std::vector<std::string> warnings;
};

/// @brief DAG-based pipeline graph engine
///
/// Upgrade from linear MediaPipeline to a full directed acyclic graph.
/// Supports fan-in (Mixer: multiple inputs → one output) and
/// fan-out (Tee: one input → multiple outputs).
///
/// @code
/// PipelineGraph graph("broadcast");
///
/// auto cam = graph.AddNode("camera", NodeType::Source, cameraSource);
/// auto ndi = graph.AddNode("ndi-in", NodeType::Source, ndiSource);
/// auto mix = graph.AddNode("mixer", NodeType::Mixer, videoMixer);
/// auto enc = graph.AddNode("h264", NodeType::Encoder, h264Encoder);
/// auto tee = graph.AddNode("tee", NodeType::Tee, nullptr);
/// auto rtmp = graph.AddNode("rtmp", NodeType::Output, rtmpOutput);
/// auto file = graph.AddNode("file", NodeType::Output, fileOutput);
///
/// graph.Connect(cam, mix);     // camera → mixer
/// graph.Connect(ndi, mix);     // ndi → mixer (fan-in)
/// graph.Connect(mix, enc);     // mixer → encoder
/// graph.Connect(enc, tee);     // encoder → tee
/// graph.Connect(tee, rtmp);    // tee → rtmp (fan-out)
/// graph.Connect(tee, file);    // tee → file (fan-out)
///
/// auto result = graph.Validate();
/// if (result.valid) graph.Build();
/// @endcode
class PipelineGraph {
public:
    explicit PipelineGraph(std::string_view name = "graph");
    ~PipelineGraph();

    PipelineGraph(const PipelineGraph&) = delete;
    PipelineGraph& operator=(const PipelineGraph&) = delete;
    PipelineGraph(PipelineGraph&&) noexcept;
    PipelineGraph& operator=(PipelineGraph&&) noexcept;

    // --- Node Management ---

    /// @brief Add a node to the graph
    /// @return Node ID
    [[nodiscard]] NodeId AddNode(
        std::string_view name,
        NodeType type,
        std::shared_ptr<IMediaObject> mediaObject = nullptr);

    /// @brief Remove a node (and all its edges)
    [[nodiscard]] VoidResult RemoveNode(NodeId id);

    /// @brief Get a node by ID
    [[nodiscard]] const GraphNode* GetNode(NodeId id) const;

    /// @brief Get all nodes
    [[nodiscard]] std::vector<GraphNode> GetNodes() const;

    /// @brief Get node count
    [[nodiscard]] size_t GetNodeCount() const;

    // --- Edge Management ---

    /// @brief Connect two nodes (source → target)
    [[nodiscard]] VoidResult Connect(NodeId source, NodeId target,
                                      uint32_t sourcePin = 0,
                                      uint32_t targetPin = 0);

    /// @brief Disconnect two nodes
    [[nodiscard]] VoidResult Disconnect(NodeId source, NodeId target);

    /// @brief Get all edges
    [[nodiscard]] std::vector<GraphEdge> GetEdges() const;

    // --- Validation ---

    /// @brief Validate the graph (cycle detection, type checking)
    [[nodiscard]] GraphValidation Validate() const;

    /// @brief Check if graph has cycles
    [[nodiscard]] bool HasCycles() const;

    // --- Lifecycle ---

    /// @brief Build the validated graph (prepare for execution)
    [[nodiscard]] VoidResult Build();

    /// @brief Start execution of the graph
    [[nodiscard]] VoidResult Start();

    /// @brief Stop execution of the graph
    [[nodiscard]] VoidResult Stop();

    /// @brief Pause execution
    [[nodiscard]] VoidResult Pause();

    /// @brief Resume execution
    [[nodiscard]] VoidResult Resume();

    /// @brief Get current pipeline state
    [[nodiscard]] PipelineState GetState() const;

    /// @brief Get the topological execution order
    [[nodiscard]] Result<std::vector<NodeId>> GetExecutionOrder() const;

    // --- Serialization ---

    /// @brief Serialize graph to JSON string
    [[nodiscard]] Result<std::string> ToJson() const;

    /// @brief Deserialize graph from JSON string
    [[nodiscard]] static Result<PipelineGraph> FromJson(std::string_view json);

    // --- Info ---

    /// @brief Get graph name
    [[nodiscard]] std::string GetName() const;

    /// @brief Get source nodes (nodes with no inputs)
    [[nodiscard]] std::vector<NodeId> GetSourceNodes() const;

    /// @brief Get output nodes (nodes with no outputs)
    [[nodiscard]] std::vector<NodeId> GetOutputNodes() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
