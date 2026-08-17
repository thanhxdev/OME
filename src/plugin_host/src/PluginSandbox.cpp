/// @file PluginSandbox.cpp
/// @brief Plugin crash isolation implementation

#include <openmedia/plugin_host/PluginSandbox.h>
#include <openmedia/core/Logger.h>

#include <atomic>
#include <future>
#include <mutex>

#ifdef _WIN32
#include <windows.h>
#endif

namespace openmedia::plugin_host {

namespace {
auto& Log() { return core::Logger::Get("plugin.sandbox"); }
}

struct PluginSandbox::Impl {
    SandboxConfig config;
    std::atomic<uint64_t> caughtExceptions{0};
    std::atomic<uint64_t> timeouts{0};
};

PluginSandbox::PluginSandbox(const SandboxConfig& config)
    : m_impl(std::make_unique<Impl>()) {
    m_impl->config = config;
}

PluginSandbox::~PluginSandbox() = default;

SandboxReport PluginSandbox::Execute(
    std::string_view pluginName,
    std::function<void()> fn) {
    return Execute(pluginName, std::move(fn), m_impl->config.defaultTimeout);
}

#ifdef _WIN32
// Helper to isolate SEH and avoid C2712/C2713 (object unwinding conflict)
struct SEHContext {
    std::function<void()>* fn;
};

static SandboxResult RunWithSEH(SEHContext* ctx, uint32_t& outCode) {
    __try {
        (*ctx->fn)();
        return SandboxResult::Success;
    } __except (outCode = GetExceptionCode(), EXCEPTION_EXECUTE_HANDLER) {
        return SandboxResult::SEHException;
    }
}
#endif

SandboxReport PluginSandbox::Execute(
    std::string_view pluginName,
    std::function<void()> fn,
    std::chrono::milliseconds timeout) {

    SandboxReport report;
    report.pluginName = std::string(pluginName);

    auto startTime = std::chrono::steady_clock::now();

    // Run with timeout using async
    auto future = std::async(std::launch::async, [&]() -> SandboxReport {
        SandboxReport innerReport;
        innerReport.pluginName = std::string(pluginName);

#ifdef _WIN32
        if (m_impl->config.enableSEH) {
            uint32_t sehCode = 0;
            SEHContext ctx{&fn};
            SandboxResult sehResult = RunWithSEH(&ctx, sehCode);
            
            if (sehResult == SandboxResult::Success) {
                innerReport.result = SandboxResult::Success;
            } else {
                innerReport.result = SandboxResult::SEHException;
                innerReport.exceptionCode = sehCode;
                innerReport.errorMessage =
                    "SEH exception 0x" + std::to_string(sehCode) +
                    " in plugin '" + std::string(pluginName) + "'";
                m_impl->caughtExceptions.fetch_add(1);
            }
        } else {
            try {
                fn();
                innerReport.result = SandboxResult::Success;
            } catch (const std::exception& e) {
                innerReport.result = SandboxResult::Exception;
                innerReport.errorMessage =
                    "Exception in plugin '" + std::string(pluginName) + "': " + e.what();
                m_impl->caughtExceptions.fetch_add(1);
            } catch (...) {
                innerReport.result = SandboxResult::Unknown;
                innerReport.errorMessage =
                    "Unknown exception in plugin '" + std::string(pluginName) + "'";
                m_impl->caughtExceptions.fetch_add(1);
            }
        }
#else
        try {
            fn();
            innerReport.result = SandboxResult::Success;
        } catch (const std::exception& e) {
            innerReport.result = SandboxResult::Exception;
            innerReport.errorMessage =
                "Exception in plugin '" + std::string(pluginName) + "': " + e.what();
            m_impl->caughtExceptions.fetch_add(1);
        } catch (...) {
            innerReport.result = SandboxResult::Unknown;
            innerReport.errorMessage =
                "Unknown exception in plugin '" + std::string(pluginName) + "'";
            m_impl->caughtExceptions.fetch_add(1);
        }
#endif

        return innerReport;
    });

    // Wait with timeout
    auto status = future.wait_for(timeout);
    auto endTime = std::chrono::steady_clock::now();
    report.elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime);

    if (status == std::future_status::timeout) {
        report.result = SandboxResult::Timeout;
        report.errorMessage =
            "Plugin '" + std::string(pluginName) + "' exceeded timeout of " +
            std::to_string(timeout.count()) + "ms";
        m_impl->timeouts.fetch_add(1);

        if (m_impl->config.logExceptions) {
            Log().Error("{}", report.errorMessage);
        }

        return report;
    }

    report = future.get();
    report.elapsed = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime);

    if (report.result != SandboxResult::Success && m_impl->config.logExceptions) {
        Log().Error("{}", report.errorMessage);
    }

    return report;
}

uint64_t PluginSandbox::GetCaughtExceptionCount() const {
    return m_impl->caughtExceptions.load();
}

uint64_t PluginSandbox::GetTimeoutCount() const {
    return m_impl->timeouts.load();
}

} // namespace openmedia::plugin_host
