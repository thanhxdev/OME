#pragma once

/// @file Logger.h
/// @brief Structured logging wrapper for OpenMedia SDK
/// @since 1.0.0

#include <memory>
#include <string>
#include <string_view>

#include <spdlog/spdlog.h>
#include <fmt/core.h>

namespace openmedia::core {

/// @brief Log levels
enum class LogLevel : uint32_t {
    Trace = 0,
    Debug,
    Info,
    Warn,
    Error,
    Critical,
    Off,
};

/// @brief Structured logging wrapper around spdlog
///
/// Instance API:
/// @code
/// auto& log = Logger::Get("core");
/// log.Info("Pipeline created: id={}", pipelineId);
/// @endcode
///
/// Static convenience API:
/// @code
/// Logger::Info("core", "Pipeline created: id={}", pipelineId);
/// @endcode
class Logger {
public:
    /// @brief Get or create a named logger
    static Logger& Get(std::string_view name = "default");

    /// @brief Initialize logging system
    static void Initialize(
        LogLevel logLevel = LogLevel::Debug,
        bool logToConsole = true,
        bool logToFile = true,
        std::string_view logDir = "./logs"
    );

    /// @brief Overload for Initialize that takes a name (for ServerApp compat)
    static void Initialize(std::string_view /*name*/,
                           LogLevel logLevel = LogLevel::Debug) {
        Initialize(logLevel);
    }

    /// @brief Shutdown logging system
    static void Shutdown();

    /// @brief Set global log level
    static void SetLevel(LogLevel level);

    // --- Instance logging methods ---
    // Uses fmt::runtime() to support runtime format strings with spdlog v12/fmt v12

    template <typename... Args>
    void Trace(std::string_view fmtStr, Args&&... args) {
        GetSpdLogger()->trace(fmt::runtime(fmtStr), std::forward<Args>(args)...);
    }

    template <typename... Args>
    void Debug(std::string_view fmtStr, Args&&... args) {
        GetSpdLogger()->debug(fmt::runtime(fmtStr), std::forward<Args>(args)...);
    }

    template <typename... Args>
    void Info(std::string_view fmtStr, Args&&... args) {
        GetSpdLogger()->info(fmt::runtime(fmtStr), std::forward<Args>(args)...);
    }

    template <typename... Args>
    void Warn(std::string_view fmtStr, Args&&... args) {
        GetSpdLogger()->warn(fmt::runtime(fmtStr), std::forward<Args>(args)...);
    }

    template <typename... Args>
    void Error(std::string_view fmtStr, Args&&... args) {
        GetSpdLogger()->error(fmt::runtime(fmtStr), std::forward<Args>(args)...);
    }

    template <typename... Args>
    void Critical(std::string_view fmtStr, Args&&... args) {
        GetSpdLogger()->critical(fmt::runtime(fmtStr), std::forward<Args>(args)...);
    }

    // --- Static convenience methods (module, message, args...) ---
    template <typename... Args>
    static void STrace(std::string_view module, std::string_view fmtStr, Args&&... args) {
        Get(module).Trace(fmtStr, std::forward<Args>(args)...);
    }

    template <typename... Args>
    static void SDebug(std::string_view module, std::string_view fmtStr, Args&&... args) {
        Get(module).Debug(fmtStr, std::forward<Args>(args)...);
    }

    template <typename... Args>
    static void SInfo(std::string_view module, std::string_view fmtStr, Args&&... args) {
        Get(module).Info(fmtStr, std::forward<Args>(args)...);
    }

    template <typename... Args>
    static void SWarn(std::string_view module, std::string_view fmtStr, Args&&... args) {
        Get(module).Warn(fmtStr, std::forward<Args>(args)...);
    }

    template <typename... Args>
    static void SError(std::string_view module, std::string_view fmtStr, Args&&... args) {
        Get(module).Error(fmtStr, std::forward<Args>(args)...);
    }

    template <typename... Args>
    static void SCritical(std::string_view module, std::string_view fmtStr, Args&&... args) {
        Get(module).Critical(fmtStr, std::forward<Args>(args)...);
    }

    /// @brief Get the underlying spdlog logger
    [[nodiscard]] std::shared_ptr<spdlog::logger> GetSpdLogger() const;

    ~Logger();

private:
    explicit Logger(std::string_view name);

    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

/// @brief Convenience macros for logging with source context
#define OME_LOG_TRACE(logger, ...)    (logger).Trace(__VA_ARGS__)
#define OME_LOG_DEBUG(logger, ...)    (logger).Debug(__VA_ARGS__)
#define OME_LOG_INFO(logger, ...)     (logger).Info(__VA_ARGS__)
#define OME_LOG_WARN(logger, ...)     (logger).Warn(__VA_ARGS__)
#define OME_LOG_ERROR(logger, ...)    (logger).Error(__VA_ARGS__)
#define OME_LOG_CRITICAL(logger, ...) (logger).Critical(__VA_ARGS__)

} // namespace openmedia::core
