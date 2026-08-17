#pragma once

#include "IPlugin.h"

namespace openmedia::plugin {

class IOverlayPlugin : public IPlugin {
public:
    virtual bool RenderOverlay(void* renderContext) = 0;
    virtual void UpdateData(const char* jsonData) = 0;
};

} // namespace openmedia::plugin
