/// @file Config.cpp
/// @brief Configuration manager implementation

#include <openmedia/core/Config.h>

#include <algorithm>
#include <cstdlib>
#include <fstream>
#include <mutex>
#include <sstream>

namespace openmedia::core {

struct Config::Impl {
    EnvironmentTag tag = EnvironmentTag::Demo;
    std::unordered_map<std::string, std::string> values;
    mutable std::mutex mutex;
};

Config::Config() : m_impl(std::make_unique<Impl>()) {
    // Try to detect environment from system
    LoadFromSystem();
}

Config::~Config() = default;

Config& Config::Instance() {
    static Config instance;
    return instance;
}

bool Config::LoadFromFile(std::string_view filePath) {
    std::lock_guard lock(m_impl->mutex);

    std::ifstream file{std::string{filePath}};
    if (!file.is_open()) return false;

    std::string line;
    while (std::getline(file, line)) {
        // Trim
        auto start = line.find_first_not_of(" \t\r\n");
        if (start == std::string::npos) continue;
        line = line.substr(start);

        // Skip comments
        if (line.empty() || line[0] == '#') continue;

        // Parse KEY=VALUE
        auto eqPos = line.find('=');
        if (eqPos == std::string::npos) continue;

        auto key = line.substr(0, eqPos);
        auto value = line.substr(eqPos + 1);

        // Trim key and value
        auto trimStr = [](std::string& s) {
            s.erase(0, s.find_first_not_of(" \t\r\n"));
            s.erase(s.find_last_not_of(" \t\r\n") + 1);
        };
        trimStr(key);
        trimStr(value);

        m_impl->values[key] = value;
    }

    // Detect environment tag
    auto tagIt = m_impl->values.find("OME_ENV_TAG");
    if (tagIt != m_impl->values.end()) {
        m_impl->tag = (tagIt->second == "production")
            ? EnvironmentTag::Production
            : EnvironmentTag::Demo;
    }

    return true;
}

void Config::LoadFromSystem() {
    // Check OME_ENV_TAG environment variable
    const char* envTag = std::getenv("OME_ENV_TAG");
    if (envTag) {
        std::string tag(envTag);
        std::transform(tag.begin(), tag.end(), tag.begin(), ::tolower);
        m_impl->tag = (tag == "production")
            ? EnvironmentTag::Production
            : EnvironmentTag::Demo;
        m_impl->values["OME_ENV_TAG"] = tag;
    }
}

EnvironmentTag Config::GetTag() const {
    return m_impl->tag;
}

std::string Config::GetTagString() const {
    return m_impl->tag == EnvironmentTag::Production ? "production" : "demo";
}

bool Config::IsDemo() const {
    return m_impl->tag == EnvironmentTag::Demo;
}

bool Config::IsProduction() const {
    return m_impl->tag == EnvironmentTag::Production;
}

std::string Config::Get(std::string_view key, std::string_view defaultValue) const {
    std::lock_guard lock(m_impl->mutex);
    auto it = m_impl->values.find(std::string(key));
    if (it != m_impl->values.end()) return it->second;

    // Fallback to system env
    const char* envVal = std::getenv(std::string(key).c_str());
    if (envVal) return std::string(envVal);

    return std::string(defaultValue);
}

int Config::GetInt(std::string_view key, int defaultValue) const {
    auto val = Get(key);
    if (val.empty()) return defaultValue;
    try {
        return std::stoi(val);
    } catch (...) {
        return defaultValue;
    }
}

bool Config::GetBool(std::string_view key, bool defaultValue) const {
    auto val = Get(key);
    if (val.empty()) return defaultValue;
    return val == "true" || val == "1" || val == "yes";
}

double Config::GetDouble(std::string_view key, double defaultValue) const {
    auto val = Get(key);
    if (val.empty()) return defaultValue;
    try {
        return std::stod(val);
    } catch (...) {
        return defaultValue;
    }
}

bool Config::IsFeatureEnabled(std::string_view featureName) const {
    auto key = "OME_FEATURE_" + std::string(featureName);
    auto val = Get(key, "false");
    return val == "true" || val == "license";
}

void Config::Set(std::string_view key, std::string_view value) {
    std::lock_guard lock(m_impl->mutex);
    m_impl->values[std::string(key)] = std::string(value);
}

bool Config::Has(std::string_view key) const {
    std::lock_guard lock(m_impl->mutex);
    return m_impl->values.contains(std::string(key));
}

} // namespace openmedia::core
