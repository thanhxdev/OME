#pragma once

#include <string>
#include <memory>

namespace openmedia::overlay {

enum class OverlayType {
    Text,
    Image,
    Custom
};

class IOverlay {
public:
    virtual ~IOverlay() = default;

    virtual std::string GetId() const = 0;
    virtual OverlayType GetType() const = 0;
    
    virtual int GetX() const = 0;
    virtual int GetY() const = 0;
    virtual void SetPosition(int x, int y) = 0;
    
    virtual int GetZOrder() const = 0;
    virtual void SetZOrder(int zOrder) = 0;

    virtual bool IsVisible() const = 0;
    virtual void SetVisible(bool visible) = 0;

    /// @brief Get the libavfilter string representation for this overlay
    /// @param inputPadName The name of the input video pad in the filter graph
    /// @param outputPadName The name of the output pad to generate
    /// @return The filter string (e.g. "drawtext=fontfile=Arial:text='Hello':x=10:y=10")
    virtual std::string GetFilterString(const std::string& inputPadName, const std::string& outputPadName) const = 0;
};

} // namespace openmedia::overlay
