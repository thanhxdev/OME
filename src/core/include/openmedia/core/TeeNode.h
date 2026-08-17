#pragma once

#include <openmedia/core/IMediaObject.h>
#include <openmedia/core/MediaFrame.h>
#include <memory>
#include <mutex>
#include <vector>

namespace openmedia::core {

/// @brief A Tee node for pipeline fan-out (1 input to N outputs)
class TeeNode : public IMediaObject {
public:
    TeeNode();
    ~TeeNode() override;

    std::string GetName() const override { return "TeeNode"; }
    PipelineState GetState() const override;

    VoidResult Initialize() override;
    VoidResult Start() override;
    VoidResult Stop() override;

    /// @brief Push frame broadcasts to all connected downstreams
    VoidResult PushFrame(std::shared_ptr<MediaFrame> frame) override;
    
    Result<std::shared_ptr<MediaFrame>> PullFrame() override;

    /// @brief Connect an additional downstream node (supports multiple)
    VoidResult Connect(std::shared_ptr<IMediaObject> downstream) override;
    
    /// @brief Disconnect a specific downstream node
    VoidResult Disconnect(std::shared_ptr<IMediaObject> downstream);
    
    /// @brief Disconnects all downstream nodes
    VoidResult Disconnect() override;
    
    void OnStateChange(StateChangeCallback callback) override;
    void OnError(ErrorCallback callback) override;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
