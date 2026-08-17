#pragma once

/// @file PluginSandbox.h
/// @brief Plugin crash isolation via SEH and timeout watchdog
/// @since 1.0.0

#include <openmedia/core/ErrorCodes.h>

#include <chrono>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>

namespace openmedia::plugin_host {

/// @brief Result of a sandboxed call
enum class SandboxResult : uint32_t {
    Success = 0,
    Exception,      ///< Plugin threw a C++ exception
    SEHException,   ///< Plugin caused a structured exception (crash)
    Timeout,        ///< Plugin exceeded time limit
    Unknown,        ///< Unknown failure
};

/// @brief Sandbox execution report
struct SandboxReport {
    SandboxResult result = SandboxResult::Success;
    std::string pluginName;
    std::string errorMessage;
    uint32_t exceptionCode = 0;     ///< SEH exception code (Windows)
    std::chrono::milliseconds elapsed{0};
};

/// @brief Sandbox configuration
struct SandboxConfig {
    std::chrono::milliseconds defaultTimeout{5000};  ///< Default timeout for plugin calls
    bool enableSEH = true;          ///< Enable Structured Exception Handling (Windows)
    bool logExceptions = true;      ///< Log caught exceptions
};

/// @brief Plugin crash isolation sandbox
///
/// Wraps plugin function calls with exception handling and timeout watchdog
/// to prevent a misbehaving plugin from crashing the server process.
///
/// @code
/// PluginSandbox sandbox({.defaultTimeout = std::chrono::seconds(5)});
///
/// auto report = sandbox.Execute("MyPlugin", [&]() {
///     plugin->ProcessFrame(frame);
/// });
///
/// if (report.result != SandboxResult::Success) {
///     LOG_ERROR("Plugin crashed: {}", report.errorMessage);
/// }
/// @endcode
class PluginSandbox {
public:
    explicit PluginSandbox(const SandboxConfig& config = {});
    ~PluginSandbox();

    PluginSandbox(const PluginSandbox&) = delete;
    PluginSandbox& operator=(const PluginSandbox&) = delete;

    /// @brief Execute a function in the sandbox with default timeout
    [[nodiscard]] SandboxReport Execute(
        std::string_view pluginName,
        std::function<void()> fn);

    /// @brief Execute a function in the sandbox with custom timeout
    [[nodiscard]] SandboxReport Execute(
        std::string_view pluginName,
        std::function<void()> fn,
        std::chrono::milliseconds timeout);

    /// @brief Execute and return a value
    template <typename T>
    [[nodiscard]] core::Result<T> ExecuteWithResult(
        std::string_view pluginName,
        std::function<T()> fn);

    /// @brief Get number of caught exceptions
    [[nodiscard]] uint64_t GetCaughtExceptionCount() const;

    /// @brief Get number of timeouts
    [[nodiscard]] uint64_t GetTimeoutCount() const;

private:
    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

// Template implementation
template <typename T>
core::Result<T> PluginSandbox::ExecuteWithResult(
    std::string_view pluginName,
    std::function<T()> fn) {
    T result{};
    auto report = Execute(pluginName, [&]() { result = fn(); });
    if (report.result != SandboxResult::Success) {
        return std::unexpected(core::Error{
            core::ErrorCode::PluginCrashed,
            report.errorMessage,
            std::string(pluginName)});
    }
    return result;
}

} // namespace openmedia::plugin_host
