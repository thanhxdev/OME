#pragma once

/// @file MediaMetadata.h
/// @brief Metadata key-value store for media frames and streams
/// @since 1.0.0

#include <any>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>

namespace openmedia::core {

/// @brief Key-value metadata store for media objects
///
/// Stores arbitrary metadata like codec info, stream properties,
/// frame-level timing, and user-defined tags.
class MediaMetadata {
public:
    MediaMetadata() = default;
    ~MediaMetadata() = default;

    /// @brief Set a string metadata value
    void Set(std::string_view key, std::string_view value);

    /// @brief Set an integer metadata value
    void SetInt(std::string_view key, int64_t value);

    /// @brief Set a double metadata value
    void SetDouble(std::string_view key, double value);

    /// @brief Get a string metadata value
    [[nodiscard]] std::optional<std::string> Get(std::string_view key) const;

    /// @brief Get an integer metadata value
    [[nodiscard]] std::optional<int64_t> GetInt(std::string_view key) const;

    /// @brief Get a double metadata value
    [[nodiscard]] std::optional<double> GetDouble(std::string_view key) const;

    /// @brief Check if key exists
    [[nodiscard]] bool Has(std::string_view key) const;

    /// @brief Remove a key
    void Remove(std::string_view key);

    /// @brief Clear all metadata
    void Clear();

    /// @brief Get number of entries
    [[nodiscard]] size_t Size() const;

    /// @brief Merge metadata from another instance
    void Merge(const MediaMetadata& other);

    /// @brief Get all keys
    [[nodiscard]] std::vector<std::string> GetKeys() const;

private:
    std::unordered_map<std::string, std::any> m_data;
};

} // namespace openmedia::core
