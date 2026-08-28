/// @file PipelineGraph.cpp
/// @brief DAG-based pipeline graph implementation

#include <openmedia/core/PipelineGraph.h>
#include <openmedia/core/Logger.h>

#include <algorithm>
#include <mutex>
#include <queue>
#include <set>
#include <sstream>
#include <unordered_map>
#include <unordered_set>

#include <nlohmann/json.hpp>

namespace openmedia::core {

namespace {
auto& Log() { return Logger::Get("pipeline.graph"); }
}

struct PipelineGraph::Impl {
    std::string name;
    uint32_t nextNodeId = 1;
    mutable std::recursive_mutex mutex;

    std::unordered_map<NodeId, GraphNode> nodes;
    std::vector<GraphEdge> edges;

    bool built = false;
    PipelineState state = PipelineState::Idle;
};

PipelineGraph::PipelineGraph(std::string_view name)
    : m_impl(std::make_unique<Impl>()) {
    m_impl->name = std::string(name);
}

PipelineGraph::~PipelineGraph() = default;
PipelineGraph::PipelineGraph(PipelineGraph&&) noexcept = default;
PipelineGraph& PipelineGraph::operator=(PipelineGraph&&) noexcept = default;

NodeId PipelineGraph::AddNode(
    std::string_view name,
    NodeType type,
    std::shared_ptr<IMediaObject> mediaObject) {

    std::lock_guard lock(m_impl->mutex);
    NodeId id = m_impl->nextNodeId++;

    GraphNode node;
    node.id = id;
    node.name = std::string(name);
    node.type = type;
    node.mediaObject = std::move(mediaObject);

    m_impl->nodes[id] = std::move(node);
    m_impl->built = false;

    Log().Debug("Added node '{}' (id={}, type={})", name, id, static_cast<uint32_t>(type));

    return id;
}

VoidResult PipelineGraph::RemoveNode(NodeId id) {
    std::lock_guard lock(m_impl->mutex);

    auto it = m_impl->nodes.find(id);
    if (it == m_impl->nodes.end()) {
        return std::unexpected(OME_ERROR(
            ErrorCode::PipelineNodeNotFound,
            "Node not found: " + std::to_string(id)));
    }

    // Remove all edges involving this node
    std::erase_if(m_impl->edges, [id](const GraphEdge& e) {
        return e.source == id || e.target == id;
    });

    // Remove from other nodes' input/output lists
    for (auto& [nid, node] : m_impl->nodes) {
        std::erase(node.inputs, id);
        std::erase(node.outputs, id);
    }

    m_impl->nodes.erase(it);
    m_impl->built = false;

    return {};
}

const GraphNode* PipelineGraph::GetNode(NodeId id) const {
    std::lock_guard lock(m_impl->mutex);
    auto it = m_impl->nodes.find(id);
    return (it != m_impl->nodes.end()) ? &it->second : nullptr;
}

std::vector<GraphNode> PipelineGraph::GetNodes() const {
    std::lock_guard lock(m_impl->mutex);
    std::vector<GraphNode> result;
    result.reserve(m_impl->nodes.size());
    for (const auto& [id, node] : m_impl->nodes) {
        result.push_back(node);
    }
    return result;
}

size_t PipelineGraph::GetNodeCount() const {
    std::lock_guard lock(m_impl->mutex);
    return m_impl->nodes.size();
}

VoidResult PipelineGraph::Connect(
    NodeId source, NodeId target,
    uint32_t sourcePin, uint32_t targetPin) {

    std::lock_guard lock(m_impl->mutex);

    auto srcIt = m_impl->nodes.find(source);
    auto tgtIt = m_impl->nodes.find(target);

    if (srcIt == m_impl->nodes.end()) {
        return std::unexpected(OME_ERROR(
            ErrorCode::PipelineNodeNotFound,
            "Source node not found: " + std::to_string(source)));
    }
    if (tgtIt == m_impl->nodes.end()) {
        return std::unexpected(OME_ERROR(
            ErrorCode::PipelineNodeNotFound,
            "Target node not found: " + std::to_string(target)));
    }

    // Check for duplicate edge
    for (const auto& edge : m_impl->edges) {
        if (edge.source == source && edge.target == target &&
            edge.sourcePin == sourcePin && edge.targetPin == targetPin) {
            return std::unexpected(OME_ERROR(
                ErrorCode::AlreadyExists,
                "Edge already exists"));
        }
    }

    GraphEdge edge{source, target, sourcePin, targetPin};
    m_impl->edges.push_back(edge);

    srcIt->second.outputs.push_back(target);
    tgtIt->second.inputs.push_back(source);

    m_impl->built = false;

    Log().Debug("Connected {} → {}", source, target);

    return {};
}

VoidResult PipelineGraph::Disconnect(NodeId source, NodeId target) {
    std::lock_guard lock(m_impl->mutex);

    bool found = false;
    std::erase_if(m_impl->edges, [&](const GraphEdge& e) {
        if (e.source == source && e.target == target) {
            found = true;
            return true;
        }
        return false;
    });

    if (!found) {
        return std::unexpected(OME_ERROR(
            ErrorCode::NotFound,
            "Edge not found"));
    }

    auto srcIt = m_impl->nodes.find(source);
    auto tgtIt = m_impl->nodes.find(target);
    if (srcIt != m_impl->nodes.end()) std::erase(srcIt->second.outputs, target);
    if (tgtIt != m_impl->nodes.end()) std::erase(tgtIt->second.inputs, source);

    m_impl->built = false;

    return {};
}

std::vector<GraphEdge> PipelineGraph::GetEdges() const {
    std::lock_guard lock(m_impl->mutex);
    return m_impl->edges;
}

bool PipelineGraph::HasCycles() const {
    std::lock_guard lock(m_impl->mutex);

    // Kahn's algorithm for topological sort — detect cycles
    std::unordered_map<NodeId, uint32_t> inDegree;
    for (const auto& [id, node] : m_impl->nodes) {
        inDegree[id] = static_cast<uint32_t>(node.inputs.size());
    }

    std::queue<NodeId> zeroIn;
    for (const auto& [id, deg] : inDegree) {
        if (deg == 0) zeroIn.push(id);
    }

    uint32_t visited = 0;
    while (!zeroIn.empty()) {
        auto current = zeroIn.front();
        zeroIn.pop();
        visited++;

        auto it = m_impl->nodes.find(current);
        if (it == m_impl->nodes.end()) continue;

        for (auto outId : it->second.outputs) {
            inDegree[outId]--;
            if (inDegree[outId] == 0) {
                zeroIn.push(outId);
            }
        }
    }

    return visited != m_impl->nodes.size();
}

GraphValidation PipelineGraph::Validate() const {
    std::lock_guard lock(m_impl->mutex);

    GraphValidation result;
    result.valid = true;

    // Check for empty graph
    // Allowing empty graph to support dynamic node addition later.
    if (m_impl->nodes.empty()) {
        // Just return valid, it's fine for it to be empty initially
        return result;
    }

    // Check for cycles
    if (HasCycles()) {
        result.valid = false;
        result.errors.push_back("Graph contains cycles");
    }

    // Check for source nodes
    bool hasSource = false;
    for (const auto& [id, node] : m_impl->nodes) {
        if (node.type == NodeType::Source) {
            hasSource = true;
            if (!node.inputs.empty()) {
                result.warnings.push_back(
                    "Source node '" + node.name + "' has input connections");
            }
        }
    }

    if (!hasSource) {
        result.valid = false;
        result.errors.push_back("Graph has no source nodes");
    }

    // Check for output nodes
    bool hasOutput = false;
    for (const auto& [id, node] : m_impl->nodes) {
        if (node.type == NodeType::Output) {
            hasOutput = true;
            if (!node.outputs.empty()) {
                result.warnings.push_back(
                    "Output node '" + node.name + "' has output connections");
            }
        }
    }

    if (!hasOutput) {
        result.warnings.push_back("Graph has no output nodes");
    }

    // Check for disconnected nodes
    for (const auto& [id, node] : m_impl->nodes) {
        if (node.inputs.empty() && node.outputs.empty() && node.type != NodeType::Source) {
            result.warnings.push_back(
                "Node '" + node.name + "' is disconnected");
        }
    }

    return result;
}

VoidResult PipelineGraph::Build() {
    auto validation = Validate();
    if (!validation.valid) {
        std::string errorMsg;
        for (const auto& err : validation.errors) {
            errorMsg += err + "; ";
        }
        return std::unexpected(OME_ERROR(
            ErrorCode::PipelineInvalidGraph, errorMsg));
    }

    m_impl->built = true;
    m_impl->state = PipelineState::Ready;

    Log().Info("Graph '{}' built: {} nodes, {} edges",
              m_impl->name, m_impl->nodes.size(), m_impl->edges.size());

    return {};
}

VoidResult PipelineGraph::Start() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Ready &&
        m_impl->state != PipelineState::Paused &&
        m_impl->state != PipelineState::Stopped) {
        return std::unexpected(OME_ERROR(
            ErrorCode::InvalidState, "Graph must be Ready, Paused, or Stopped to start"));
    }

    if (!m_impl->built) {
        return std::unexpected(OME_ERROR(
            ErrorCode::InvalidState, "Graph must be built before starting"));
    }

    Log().Info("Graph '{}' starting...", m_impl->name);

    // Get topological order
    auto orderResult = GetExecutionOrder();
    if (!orderResult) {
        m_impl->state = PipelineState::Error;
        return std::unexpected(orderResult.error());
    }

    // Start nodes in topological order
    for (auto id : *orderResult) {
        if (auto obj = m_impl->nodes[id].mediaObject) {
            // First initialize if not already done
            if (auto initRes = obj->Initialize(); !initRes) {
                m_impl->state = PipelineState::Error;
                return initRes;
            }
            // Then start
            if (auto startRes = obj->Start(); !startRes) {
                m_impl->state = PipelineState::Error;
                return startRes;
            }
        }
    }

    m_impl->state = PipelineState::Running;
    Log().Info("Graph '{}' running", m_impl->name);
    return {};
}

VoidResult PipelineGraph::Stop() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Running &&
        m_impl->state != PipelineState::Paused) {
        return {}; // Already stopped
    }

    m_impl->state = PipelineState::Stopping;
    Log().Info("Graph '{}' stopping...", m_impl->name);

    auto orderResult = GetExecutionOrder();
    if (orderResult) {
        // Stop nodes in reverse topological order
        auto order = *orderResult;
        for (auto it = order.rbegin(); it != order.rend(); ++it) {
            if (auto obj = m_impl->nodes[*it].mediaObject) {
                auto res = obj->Stop();
                (void)res;
            }
        }
    }

    m_impl->state = PipelineState::Stopped;
    Log().Info("Graph '{}' stopped", m_impl->name);
    return {};
}

VoidResult PipelineGraph::Pause() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Running) {
        return std::unexpected(OME_ERROR(
            ErrorCode::InvalidState, "Graph must be Running to pause"));
    }

    m_impl->state = PipelineState::Paused;
    return {};
}

VoidResult PipelineGraph::Resume() {
    std::lock_guard lock(m_impl->mutex);

    if (m_impl->state != PipelineState::Paused) {
        return std::unexpected(OME_ERROR(
            ErrorCode::InvalidState, "Graph must be Paused to resume"));
    }

    m_impl->state = PipelineState::Running;
    return {};
}

PipelineState PipelineGraph::GetState() const {
    std::lock_guard lock(m_impl->mutex);
    return m_impl->state;
}

Result<std::vector<NodeId>> PipelineGraph::GetExecutionOrder() const {
    std::lock_guard lock(m_impl->mutex);

    // Kahn's algorithm for topological sort
    std::unordered_map<NodeId, uint32_t> inDegree;
    for (const auto& [id, node] : m_impl->nodes) {
        inDegree[id] = static_cast<uint32_t>(node.inputs.size());
    }

    std::queue<NodeId> zeroIn;
    for (const auto& [id, deg] : inDegree) {
        if (deg == 0) zeroIn.push(id);
    }

    std::vector<NodeId> order;
    order.reserve(m_impl->nodes.size());

    while (!zeroIn.empty()) {
        auto current = zeroIn.front();
        zeroIn.pop();
        order.push_back(current);

        auto it = m_impl->nodes.find(current);
        if (it == m_impl->nodes.end()) continue;

        for (auto outId : it->second.outputs) {
            inDegree[outId]--;
            if (inDegree[outId] == 0) {
                zeroIn.push(outId);
            }
        }
    }

    if (order.size() != m_impl->nodes.size()) {
        return std::unexpected(OME_ERROR(
            ErrorCode::PipelineCyclicGraph,
            "Cannot determine execution order: graph has cycles"));
    }

    return order;
}

Result<std::string> PipelineGraph::ToJson() const {
    std::lock_guard lock(m_impl->mutex);

    std::ostringstream ss;
    ss << "{\n";
    ss << "  \"name\": \"" << m_impl->name << "\",\n";
    ss << "  \"nodes\": [\n";

    bool firstNode = true;
    for (const auto& [id, node] : m_impl->nodes) {
        if (!firstNode) ss << ",\n";
        firstNode = false;
        ss << "    {\"id\": " << id
           << ", \"name\": \"" << node.name
           << "\", \"type\": " << static_cast<uint32_t>(node.type) << "}";
    }

    ss << "\n  ],\n  \"edges\": [\n";

    bool firstEdge = true;
    for (const auto& edge : m_impl->edges) {
        if (!firstEdge) ss << ",\n";
        firstEdge = false;
        ss << "    {\"source\": " << edge.source
           << ", \"target\": " << edge.target
           << ", \"sourcePin\": " << edge.sourcePin
           << ", \"targetPin\": " << edge.targetPin << "}";
    }

    ss << "\n  ]\n}";

    return ss.str();
}

Result<PipelineGraph> PipelineGraph::FromJson(std::string_view json) {
    try {
        auto j = nlohmann::json::parse(json);

        std::string name = j.value("name", "graph");
        PipelineGraph graph(name);

        if (j.contains("nodes") && j["nodes"].is_array()) {
            for (const auto& jnode : j["nodes"]) {
                if (!jnode.contains("name") || !jnode.contains("type")) {
                    continue;
                }
                
                std::string nodeName = jnode["name"];
                NodeType type = static_cast<NodeType>(jnode["type"].get<uint32_t>());
                
                // Add the node (mediaObject is nullptr for now, to be populated by factory)
                graph.AddNode(nodeName, type, nullptr);
                
                // If the json explicitly provides an ID, we might need to map it
                // but AddNode assigns sequential IDs starting from 1. 
                // For simplicity, we assume the IDs in JSON match the creation order (1, 2, 3...)
                // which is true if serialized by ToJson().
            }
        }

        if (j.contains("edges") && j["edges"].is_array()) {
            for (const auto& jedge : j["edges"]) {
                if (!jedge.contains("source") || !jedge.contains("target")) {
                    continue;
                }
                
                NodeId src = jedge["source"].get<NodeId>();
                NodeId tgt = jedge["target"].get<NodeId>();
                uint32_t srcPin = jedge.value("sourcePin", 0);
                uint32_t tgtPin = jedge.value("targetPin", 0);
                
                auto res = graph.Connect(src, tgt, srcPin, tgtPin);
                if (!res) return std::unexpected(res.error());
            }
        }

        return graph;
    } catch (const std::exception& e) {
        return std::unexpected(OME_ERROR(
            ErrorCode::InvalidData,
            std::string("Failed to parse PipelineGraph JSON: ") + e.what()));
    }
}

std::string PipelineGraph::GetName() const {
    return m_impl->name;
}

std::vector<NodeId> PipelineGraph::GetSourceNodes() const {
    std::lock_guard lock(m_impl->mutex);
    std::vector<NodeId> result;
    for (const auto& [id, node] : m_impl->nodes) {
        if (node.inputs.empty()) {
            result.push_back(id);
        }
    }
    return result;
}

std::vector<NodeId> PipelineGraph::GetOutputNodes() const {
    std::lock_guard lock(m_impl->mutex);
    std::vector<NodeId> result;
    for (const auto& [id, node] : m_impl->nodes) {
        if (node.outputs.empty()) {
            result.push_back(id);
        }
    }
    return result;
}

} // namespace openmedia::core
