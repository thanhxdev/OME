#pragma once

#include "IVideoFilter.h"

namespace openmedia::plugin {

class ITransitionPlugin : public IPlugin {
public:
    virtual void SetDuration(int milliseconds) = 0;
    virtual void SetProgress(float progress) = 0;
    
    // Process frames from source A and source B to create transition frame
    virtual bool ProcessTransition(
        const core::MediaFrame& frameA,
        const core::MediaFrame& frameB,
        core::MediaFrame& outputFrame
    ) = 0;
};

} // namespace openmedia::plugin
