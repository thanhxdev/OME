/// @file Logger.cpp
/// @brief Logger implementation using spdlog

#include <openmedia/core/Logger.h>

#include <spdlog/spdlog.h>
#include <spdlog/sinks/stdout_color_sinks.h>
#include <spdlog/sinks/rotating_file_sink.h>

#include <filesystem>
#include <mutex>
#include <unordered_map>

namespace openmedia::core {

struct Logger::Impl {
    std::shared_ptr<spdlog::logger> logger;
    std::string name;
};

// Global state
static std::mutex s_loggerMutex;
static std::unordered_map<std::string, std::unique_ptr<Logger>> s_loggers;
static bool s_initialized = false;
static std::vector<spdlog::sink_ptr> s_sinks;

Logger::Logger(std::string_view name) : m_impl(std::make_unique<Impl>()) {
    m_impl->name = std::string(name);

    if (s_sinks.empty()) {
        // Default: console only
        auto consoleSink = std::make_shared<spdlog::sinks::stdout_color_sink_mt>();
        m_impl->logger = std::make_shared<spdlog::logger>(m_impl->name, consoleSink);
    } else {
        m_impl->logger = std::make_shared<spdlog::logger>(
            m_impl->name, s_sinks.begin(), s_sinks.end());
    }

    m_impl->logger->set_level(spdlog::level::debug);
    spdlog::register_logger(m_impl->logger);
}

Logger::~Logger() = default;

Logger& Logger::Get(std::string_view name) {
    std::lock_guard lock(s_loggerMutex);
    auto key = std::string(name);
    auto it = s_loggers.find(key);
    if (it != s_loggers.end()) {
        return *it->second;
    }
    auto logger = std::unique_ptr<Logger>(new Logger(name));
    auto& ref = *logger;
    s_loggers[key] = std::move(logger);
    return ref;
}

void Logger::Initialize(LogLevel logLevel, bool logToConsole, bool logToFile, std::string_view logDir) {
    std::lock_guard lock(s_loggerMutex);

    s_sinks.clear();

    if (logToConsole) {
        auto consoleSink = std::make_shared<spdlog::sinks::stdout_color_sink_mt>();
        s_sinks.push_back(consoleSink);
    }

    if (logToFile) {
        auto dir = std::string(logDir);
        std::filesystem::create_directories(dir);
        auto filePath = dir + "/openmedia.log";
        auto fileSink = std::make_shared<spdlog::sinks::rotating_file_sink_mt>(
            filePath, 50 * 1024 * 1024, 10);
        s_sinks.push_back(fileSink);
    }

    // Set default level
    auto level = static_cast<spdlog::level::level_enum>(static_cast<int>(logLevel));
    spdlog::set_level(level);

    // Set pattern
    spdlog::set_pattern("[%Y-%m-%d %H:%M:%S.%e] [%l] [%t] [%n] %v");

    s_initialized = true;
}

void Logger::Shutdown() {
    std::lock_guard lock(s_loggerMutex);
    s_loggers.clear();
    s_sinks.clear();
    spdlog::shutdown();
    s_initialized = false;
}

void Logger::SetLevel(LogLevel level) {
    auto spdLevel = static_cast<spdlog::level::level_enum>(static_cast<int>(level));
    spdlog::set_level(spdLevel);
}

std::shared_ptr<spdlog::logger> Logger::GetSpdLogger() const {
    return m_impl->logger;
}

// Template implementations
template <typename... Args>
void Logger::Trace(std::string_view fmt, Args&&... args) {
    m_impl->logger->trace(fmt, std::forward<Args>(args)...);
}

template <typename... Args>
void Logger::Debug(std::string_view fmt, Args&&... args) {
    m_impl->logger->debug(fmt, std::forward<Args>(args)...);
}

template <typename... Args>
void Logger::Info(std::string_view fmt, Args&&... args) {
    m_impl->logger->info(fmt, std::forward<Args>(args)...);
}

template <typename... Args>
void Logger::Warn(std::string_view fmt, Args&&... args) {
    m_impl->logger->warn(fmt, std::forward<Args>(args)...);
}

template <typename... Args>
void Logger::Error(std::string_view fmt, Args&&... args) {
    m_impl->logger->error(fmt, std::forward<Args>(args)...);
}

template <typename... Args>
void Logger::Critical(std::string_view fmt, Args&&... args) {
    m_impl->logger->critical(fmt, std::forward<Args>(args)...);
}

// Explicit template instantiations for common types
template void Logger::Trace(std::string_view);
template void Logger::Debug(std::string_view);
template void Logger::Info(std::string_view);
template void Logger::Warn(std::string_view);
template void Logger::Error(std::string_view);
template void Logger::Critical(std::string_view);

} // namespace openmedia::core
