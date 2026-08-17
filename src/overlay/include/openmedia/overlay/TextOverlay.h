#pragma once
#include <openmedia/overlay/IOverlay.h>
#include <string>

namespace openmedia::overlay {

class TextOverlay : public IOverlay {
public:
    TextOverlay(std::string id, std::string text);
    ~TextOverlay() override = default;

    std::string GetId() const override { return m_id; }
    OverlayType GetType() const override { return OverlayType::Text; }
    
    int GetX() const override { return m_x; }
    int GetY() const override { return m_y; }
    void SetPosition(int x, int y) override { m_x = x; m_y = y; }
    
    int GetZOrder() const override { return m_zOrder; }
    void SetZOrder(int zOrder) override { m_zOrder = zOrder; }

    bool IsVisible() const override { return m_visible; }
    void SetVisible(bool visible) override { m_visible = visible; }

    // Text specific properties
    void SetText(const std::string& text) { m_text = text; }
    std::string GetText() const { return m_text; }
    
    void SetFontPath(const std::string& fontPath) { m_fontPath = fontPath; }
    std::string GetFontPath() const { return m_fontPath; }

    void SetFontSize(int size) { m_fontSize = size; }
    int GetFontSize() const { return m_fontSize; }

    void SetFontColor(const std::string& color) { m_fontColor = color; }
    std::string GetFontColor() const { return m_fontColor; }

    std::string GetFilterString(const std::string& inputPadName, const std::string& outputPadName) const override;

private:
    std::string m_id;
    std::string m_text;
    std::string m_fontPath = ""; 
    int m_fontSize = 24;
    std::string m_fontColor = "white";

    int m_x = 0;
    int m_y = 0;
    int m_zOrder = 0;
    bool m_visible = true;
};

} // namespace openmedia::overlay
