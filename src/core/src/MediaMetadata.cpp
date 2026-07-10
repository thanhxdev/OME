/// @file MediaMetadata.cpp
/// @brief MediaMetadata implementation

#include <openmedia/core/MediaMetadata.h>

namespace openmedia::core {

void MediaMetadata::Set(std::string_view key, std::string_view value) {
    m_data[std::string(key)] = std::string(value);
}

void MediaMetadata::SetInt(std::string_view key, int64_t value) {
    m_data[std::string(key)] = value;
}

void MediaMetadata::SetDouble(std::string_view key, double value) {
    m_data[std::string(key)] = value;
}

std::optional<std::string> MediaMetadata::Get(std::string_view key) const {
    auto it = m_data.find(std::string(key));
    if (it == m_data.end()) return std::nullopt;
    try {
        return std::any_cast<std::string>(it->second);
    } catch (const std::bad_any_cast&) {
        return std::nullopt;
    }
}

std::optional<int64_t> MediaMetadata::GetInt(std::string_view key) const {
    auto it = m_data.find(std::string(key));
    if (it == m_data.end()) return std::nullopt;
    try {
        return std::any_cast<int64_t>(it->second);
    } catch (const std::bad_any_cast&) {
        return std::nullopt;
    }
}

std::optional<double> MediaMetadata::GetDouble(std::string_view key) const {
    auto it = m_data.find(std::string(key));
    if (it == m_data.end()) return std::nullopt;
    try {
        return std::any_cast<double>(it->second);
    } catch (const std::bad_any_cast&) {
        return std::nullopt;
    }
}

bool MediaMetadata::Has(std::string_view key) const {
    return m_data.contains(std::string(key));
}

void MediaMetadata::Remove(std::string_view key) {
    m_data.erase(std::string(key));
}

void MediaMetadata::Clear() {
    m_data.clear();
}

size_t MediaMetadata::Size() const {
    return m_data.size();
}

void MediaMetadata::Merge(const MediaMetadata& other) {
    for (const auto& [key, val] : other.m_data) {
        m_data[key] = val;
    }
}

std::vector<std::string> MediaMetadata::GetKeys() const {
    std::vector<std::string> keys;
    keys.reserve(m_data.size());
    for (const auto& [key, _] : m_data) {
        keys.push_back(key);
    }
    return keys;
}

} // namespace openmedia::core
