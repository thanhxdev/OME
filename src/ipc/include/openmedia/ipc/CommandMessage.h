#pragma once

/// @file CommandMessage.h
/// @brief Binary serialization/deserialization helpers for IPC command payloads
/// @since 1.0.0

#include <openmedia/ipc/CommandTypes.h>
#include <openmedia/core/ErrorCodes.h>

#include <cstdint>
#include <cstring>
#include <string>
#include <string_view>
#include <type_traits>
#include <vector>

namespace openmedia::ipc {

/// @brief Binary message builder — writes primitives into a byte buffer
///
/// @code
/// MessageBuilder builder;
/// builder.WriteU32(42);
/// builder.WriteString("hello");
/// auto payload = builder.Finish();
/// @endcode
class MessageBuilder {
public:
    MessageBuilder() = default;

    /// @brief Reserve capacity
    void Reserve(size_t bytes) { m_buffer.reserve(bytes); }

    /// @brief Write unsigned 8-bit integer
    void WriteU8(uint8_t value) {
        m_buffer.push_back(value);
    }

    /// @brief Write unsigned 16-bit integer (little-endian)
    void WriteU16(uint16_t value) {
        WriteRaw(&value, sizeof(value));
    }

    /// @brief Write unsigned 32-bit integer (little-endian)
    void WriteU32(uint32_t value) {
        WriteRaw(&value, sizeof(value));
    }

    /// @brief Write unsigned 64-bit integer (little-endian)
    void WriteU64(uint64_t value) {
        WriteRaw(&value, sizeof(value));
    }

    /// @brief Write signed 32-bit integer
    void WriteI32(int32_t value) {
        WriteRaw(&value, sizeof(value));
    }

    /// @brief Write 64-bit float
    void WriteF64(double value) {
        WriteRaw(&value, sizeof(value));
    }

    /// @brief Write 32-bit float
    void WriteF32(float value) {
        WriteRaw(&value, sizeof(value));
    }

    /// @brief Write boolean
    void WriteBool(bool value) {
        WriteU8(value ? 1 : 0);
    }

    /// @brief Write length-prefixed string
    void WriteString(std::string_view str) {
        WriteU32(static_cast<uint32_t>(str.size()));
        if (!str.empty()) {
            WriteRaw(str.data(), str.size());
        }
    }

    /// @brief Write raw bytes
    void WriteBytes(const void* data, size_t size) {
        WriteU32(static_cast<uint32_t>(size));
        if (size > 0) {
            WriteRaw(data, size);
        }
    }

    /// @brief Write a trivially copyable struct
    template <typename T>
        requires std::is_trivially_copyable_v<T>
    void WriteStruct(const T& value) {
        WriteRaw(&value, sizeof(T));
    }

    /// @brief Get current buffer size
    [[nodiscard]] size_t Size() const { return m_buffer.size(); }

    /// @brief Finish and return the built payload
    [[nodiscard]] std::vector<uint8_t> Finish() { return std::move(m_buffer); }

    /// @brief Get const reference to the buffer
    [[nodiscard]] const std::vector<uint8_t>& Data() const { return m_buffer; }

private:
    void WriteRaw(const void* data, size_t size) {
        auto* bytes = static_cast<const uint8_t*>(data);
        m_buffer.insert(m_buffer.end(), bytes, bytes + size);
    }

    std::vector<uint8_t> m_buffer;
};

/// @brief Binary message reader — reads primitives from a byte buffer
///
/// @code
/// MessageReader reader(payload);
/// auto id = reader.ReadU32();
/// auto name = reader.ReadString();
/// @endcode
class MessageReader {
public:
    explicit MessageReader(const std::vector<uint8_t>& data)
        : m_data(data.data()), m_size(data.size()), m_offset(0) {}

    MessageReader(const uint8_t* data, size_t size)
        : m_data(data), m_size(size), m_offset(0) {}

    /// @brief Read unsigned 8-bit integer
    [[nodiscard]] uint8_t ReadU8() {
        uint8_t value = 0;
        ReadRaw(&value, sizeof(value));
        return value;
    }

    /// @brief Read unsigned 16-bit integer
    [[nodiscard]] uint16_t ReadU16() {
        uint16_t value = 0;
        ReadRaw(&value, sizeof(value));
        return value;
    }

    /// @brief Read unsigned 32-bit integer
    [[nodiscard]] uint32_t ReadU32() {
        uint32_t value = 0;
        ReadRaw(&value, sizeof(value));
        return value;
    }

    /// @brief Read unsigned 64-bit integer
    [[nodiscard]] uint64_t ReadU64() {
        uint64_t value = 0;
        ReadRaw(&value, sizeof(value));
        return value;
    }

    /// @brief Read signed 32-bit integer
    [[nodiscard]] int32_t ReadI32() {
        int32_t value = 0;
        ReadRaw(&value, sizeof(value));
        return value;
    }

    /// @brief Read 64-bit float
    [[nodiscard]] double ReadF64() {
        double value = 0.0;
        ReadRaw(&value, sizeof(value));
        return value;
    }

    /// @brief Read 32-bit float
    [[nodiscard]] float ReadF32() {
        float value = 0.0f;
        ReadRaw(&value, sizeof(value));
        return value;
    }

    /// @brief Read boolean
    [[nodiscard]] bool ReadBool() {
        return ReadU8() != 0;
    }

    /// @brief Read length-prefixed string
    [[nodiscard]] std::string ReadString() {
        auto length = ReadU32();
        if (length == 0 || m_offset + length > m_size) return {};
        std::string result(reinterpret_cast<const char*>(m_data + m_offset), length);
        m_offset += length;
        return result;
    }

    /// @brief Read length-prefixed bytes
    [[nodiscard]] std::vector<uint8_t> ReadBytes() {
        auto length = ReadU32();
        if (length == 0 || m_offset + length > m_size) return {};
        std::vector<uint8_t> result(m_data + m_offset, m_data + m_offset + length);
        m_offset += length;
        return result;
    }

    /// @brief Read a trivially copyable struct
    template <typename T>
        requires std::is_trivially_copyable_v<T>
    [[nodiscard]] T ReadStruct() {
        T value{};
        ReadRaw(&value, sizeof(T));
        return value;
    }

    /// @brief Check if there's more data to read
    [[nodiscard]] bool HasMore() const { return m_offset < m_size; }

    /// @brief Get remaining bytes
    [[nodiscard]] size_t Remaining() const { return m_size - m_offset; }

    /// @brief Get current read offset
    [[nodiscard]] size_t Offset() const { return m_offset; }

    /// @brief Check if read error occurred (read beyond buffer)
    [[nodiscard]] bool HasError() const { return m_error; }

private:
    void ReadRaw(void* dest, size_t size) {
        if (m_offset + size > m_size) {
            m_error = true;
            return;
        }
        std::memcpy(dest, m_data + m_offset, size);
        m_offset += size;
    }

    const uint8_t* m_data;
    size_t m_size;
    size_t m_offset;
    bool m_error = false;
};

// --- Convenience: Build common command payloads ---

/// @brief Build a CreatePipeline command payload
[[nodiscard]] inline std::vector<uint8_t> BuildCreatePipelinePayload(
    std::string_view name, uint32_t width = 1920, uint32_t height = 1080, double fps = 29.97) {
    MessageBuilder builder;
    builder.WriteString(name);
    builder.WriteU32(width);
    builder.WriteU32(height);
    builder.WriteF64(fps);
    return builder.Finish();
}

/// @brief Parse a CreatePipeline response (returns pipeline ID)
[[nodiscard]] inline uint32_t ParseCreatePipelineResponse(const std::vector<uint8_t>& payload) {
    MessageReader reader(payload);
    return reader.ReadU32();
}

/// @brief Build an OpenSource command payload
[[nodiscard]] inline std::vector<uint8_t> BuildOpenSourcePayload(
    uint32_t pipelineId, std::string_view url) {
    MessageBuilder builder;
    builder.WriteU32(pipelineId);
    builder.WriteString(url);
    return builder.Finish();
}

/// @brief Build a generic status response
[[nodiscard]] inline std::vector<uint8_t> BuildStatusResponse(
    ResponseStatus status, std::string_view message = "") {
    MessageBuilder builder;
    builder.WriteU32(static_cast<uint32_t>(status));
    builder.WriteString(message);
    return builder.Finish();
}

} // namespace openmedia::ipc
