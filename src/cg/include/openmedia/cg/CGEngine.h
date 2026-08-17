#pragma once
#include <openmedia/core/MediaFrame.h>
#include <openmedia/core/ErrorCodes.h>
#include <memory>
#include <string>

namespace openmedia::cg {

class CGEngine {
public:
    static int InitializeSubProcess();
    
    CGEngine(int width = 1920, int height = 1080);
    ~CGEngine();

    core::VoidResult LoadTemplate(const std::string& templatePath);
    core::VoidResult BindData(const std::string& key, const std::string& value);
    core::VoidResult Render(std::shared_ptr<core::MediaFrame> frame);

    // Should be called periodically to pump the CEF message loop if running on the same thread
    void DoMessageLoopWork();

private:
    struct Impl;
    std::unique_ptr<Impl> pImpl;
};

} // namespace openmedia::cg
