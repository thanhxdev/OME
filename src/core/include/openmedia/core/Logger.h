#pragma once

/// @file Logger.h
/// @brief Structured logging wrapper for OpenMedia SDK
/// @since 1.0.0

#include <memory>
#include <string>
#include <string_view>

// Forward declare spdlog to avoid header pollution
namespace spdlog {
class logger;
}

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
/// Provides module-specific logging with configurable levels,
/// console and file output, and structured log format.
///
/// @code
/// auto& log = Logger::Get("core");
/// log.Info("Pipeline created: id={}", pipelineId);
/// @endcode
class Logger {
public:
    /// @brief Get or create a named logger
    /// @param name Module name (e.g., "core", "ipc", "mixer")
    /// @return Reference to the logger
    static Logger& Get(std::string_view name = "default");

    /// @brief Initialize logging system
    /// @param logLevel Default log level
    /// @param logToConsole Enable console output
    /// @param logToFile Enable file output
    /// @param logDir Directory for log files
    static void Initialize(
        LogLevel logLevel = LogLevel::Debug,
        bool logToConsole = true,
        bool logToFile = true,
        std::string_view logDir = "./logs"
    );

    /// @brief Shutdown logging system
    static void Shutdown();

    /// @brief Set global log level
    static void SetLevel(LogLevel level);

    // Logging methods
    template <typename... Args>
    void Trace(std::string_view fmt, Args&&... args);

    template <typename... Args>
    void Debug(std::string_view fmt, Args&&... args);

    template <typename... Args>
    void Info(std::string_view fmt, Args&&... args);

    template <typename... Args>
    void Warn(std::string_view fmt, Args&&... args);

    template <typename... Args>
    void Error(std::string_view fmt, Args&&... args);

    template <typename... Args>
    void Critical(std::string_view fmt, Args&&... args);

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
