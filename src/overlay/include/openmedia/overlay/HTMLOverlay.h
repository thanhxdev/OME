#pragma once
#include <openmedia/overlay/IOverlay.h>
#include <openmedia/cg/CGEngine.h>
#include <memory>
#include <string>

namespace openmedia::overlay {

class HTMLOverlay : public IOverlay {
public:
    HTMLOverlay(std::string id, int width, int height);
    ~HTMLOverlay() override;

    std::string GetId() const override { return m_id; }
    OverlayType GetType() const override { return OverlayType::Custom; }
    
    int GetX() const override { return m_x; }
    int GetY() const override { return m_y; }
    void SetPosition(int x, int y) override { m_x = x; m_y = y; }
    
    int GetZOrder() const override { return m_zOrder; }
    void SetZOrder(int zOrder) override { m_zOrder = zOrder; }

    bool IsVisible() const override { return m_visible; }
    void SetVisible(bool visible) override { m_visible = visible; }

    // HTML/CG specific properties
    core::VoidResult LoadTemplate(const std::string& templatePath);
    core::VoidResult BindData(const std::string& key, const std::string& value);
    
    // Process CEF message loop
    void Update();

    std::shared_ptr<core::MediaFrame> GetRenderedFrame();

    std::string GetFilterString(const std::string& inputPadName, const std::string& outputPadName) const override;

private:
    std::string m_id;
    int m_x = 0;
    int m_y = 0;
    int m_zOrder = 0;
    bool m_visible = true;

    int m_width;
    int m_height;
    std::shared_ptr<cg::CGEngine> m_cgEngine;
    std::shared_ptr<core::MediaFrame> m_frameBuffer;
};

} // namespace openmedia::overlay
