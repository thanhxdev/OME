/// @file CGRenderer.h
#pragma once

#include <memory>
#include <string>
#include <openmedia/core/MediaFrame.h>

namespace openmedia::cg {

class CGEngine;

class CGRenderer {
public:
    explicit CGRenderer(std::shared_ptr<CGEngine> engine);
    ~CGRenderer();

    /// @brief Initialize the renderer
    bool Initialize();

    /// @brief Render current state of the CG to the provided frame
    /// @param frame The frame to render into
    bool Render(std::shared_ptr<core::MediaFrame> frame);

    /// @brief Update animations and state based on delta time
    /// @param deltaTimeMs Time elapsed since last render in milliseconds
    void Update(double deltaTimeMs);

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::cg
