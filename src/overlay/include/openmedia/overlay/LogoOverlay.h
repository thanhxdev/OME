#pragma once
#include <openmedia/overlay/IOverlay.h>
#include <string>

namespace openmedia::overlay {

class LogoOverlay : public IOverlay {
public:
    LogoOverlay(std::string id, std::string imagePath);
    ~LogoOverlay() override = default;

    std::string GetId() const override { return m_id; }
    OverlayType GetType() const override { return OverlayType::Image; }
    
    int GetX() const override { return m_x; }
    int GetY() const override { return m_y; }
    void SetPosition(int x, int y) override { m_x = x; m_y = y; }
    
    int GetZOrder() const override { return m_zOrder; }
    void SetZOrder(int zOrder) override { m_zOrder = zOrder; }

    bool IsVisible() const override { return m_visible; }
    void SetVisible(bool visible) override { m_visible = visible; }

    // Logo specific properties
    void SetImagePath(const std::string& imagePath) { m_imagePath = imagePath; }
    std::string GetImagePath() const { return m_imagePath; }

    void SetScale(float scale) { m_scale = scale; }
    float GetScale() const { return m_scale; }

    std::string GetFilterString(const std::string& inputPadName, const std::string& outputPadName) const override;

private:
    std::string m_id;
    std::string m_imagePath;
    float m_scale = 1.0f;

    int m_x = 0;
    int m_y = 0;
    int m_zOrder = 0;
    bool m_visible = true;
};

} // namespace openmedia::overlay
