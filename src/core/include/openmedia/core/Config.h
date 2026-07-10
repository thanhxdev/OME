#pragma once

/// @file Config.h
/// @brief JSON-based configuration loader for OpenMedia SDK
/// @since 1.0.0

#include <memory>
#include <string>
#include <string_view>
#include <unordered_map>

namespace openmedia::core {

/// @brief Environment tag
enum class EnvironmentTag {
    Demo,
    Production,
};

/// @brief Configuration manager
///
/// Loads configuration from .env files and provides typed access
/// to configuration values. Singleton pattern.
///
/// @code
/// auto& config = Config::Instance();
/// auto width = config.GetInt("OME_DEFAULT_VIDEO_WIDTH", 1920);
/// @endcode
class Config {
public:
    /// @brief Get singleton instance
    static Config& Instance();

    /// @brief Load from .env file
    /// @param filePath Path to .env file
    /// @return true on success
    bool LoadFromFile(std::string_view filePath);

    /// @brief Load from system environment variables
    void LoadFromSystem();

    /// @brief Get current environment tag
    [[nodiscard]] EnvironmentTag GetTag() const;

    /// @brief Get tag as string
    [[nodiscard]] std::string GetTagString() const;

    /// @brief Check environment type
    [[nodiscard]] bool IsDemo() const;
    [[nodiscard]] bool IsProduction() const;

    /// @brief Get config value as string
    [[nodiscard]] std::string Get(std::string_view key, std::string_view defaultValue = "") const;

    /// @brief Get config value as integer
    [[nodiscard]] int GetInt(std::string_view key, int defaultValue = 0) const;

    /// @brief Get config value as boolean
    [[nodiscard]] bool GetBool(std::string_view key, bool defaultValue = false) const;

    /// @brief Get config value as double
    [[nodiscard]] double GetDouble(std::string_view key, double defaultValue = 0.0) const;

    /// @brief Check if a feature is enabled
    [[nodiscard]] bool IsFeatureEnabled(std::string_view featureName) const;

    /// @brief Set a config value at runtime
    void Set(std::string_view key, std::string_view value);

    /// @brief Check if key exists
    [[nodiscard]] bool Has(std::string_view key) const;

    ~Config();

private:
    Config();
    Config(const Config&) = delete;
    Config& operator=(const Config&) = delete;

    struct Impl;
    std::unique_ptr<Impl> m_impl;
};

} // namespace openmedia::core
