#pragma once

#include <cstdint>
#include <vector>

namespace openmedia::metadata {

// SCTE-104 Automation to Compression Communications API
enum class Scte104OpID : uint16_t {
    InitRequest = 0x0100,
    InitResponse = 0x0101,
    AliveRequest = 0x0102,
    AliveResponse = 0x0103,
    InjectSectionRequest = 0x0104,
    InjectSectionResponse = 0x0105
};

struct Scte104Message {
    Scte104OpID opID;
    uint16_t messageSize;
    uint16_t result;
    uint16_t resultExtension;
    
    std::vector<uint8_t> payload;
};

// SCTE-104 specific payloads (MultipleOperationMessage, etc.)
struct SpliceRequestData {
    uint8_t spliceInsertType = 0;
    uint32_t spliceEventId = 0;
    uint16_t uniqueProgramId = 0;
    uint16_t preRollTime = 0;
    uint16_t breakDuration = 0;
    uint8_t availNum = 0;
    uint8_t availsExpected = 0;
    uint8_t autoReturnFlag = 0;
};

class Scte104Parser {
public:
    static bool Parse(const uint8_t* data, size_t size, Scte104Message& outMessage);
    static bool Serialize(const Scte104Message& message, std::vector<uint8_t>& outData);
};

} // namespace openmedia::metadata
