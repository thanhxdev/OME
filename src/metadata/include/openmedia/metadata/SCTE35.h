#pragma once

#include <cstdint>
#include <vector>
#include <string>

namespace openmedia::metadata {

// SCTE-35 Splice Info Section commands
enum class SpliceCommandType : uint16_t {
    SpliceNull = 0x0000,
    SpliceSchedule = 0x0004,
    SpliceInsert = 0x0005,
    TimeSignal = 0x0006,
    BandwidthReservation = 0x0007,
    PrivateCommand = 0x00FF
};

struct Scte35SpliceInfo {
    uint8_t table_id = 0xFC;
    bool section_syntax_indicator = false;
    bool private_indicator = false;
    uint16_t section_length;
    uint8_t protocol_version = 0;
    bool encrypted_packet = false;
    uint8_t encryption_algorithm = 0;
    uint64_t pts_adjustment = 0;
    uint8_t cw_index = 0;
    uint16_t tier = 0x0FFF;
    uint16_t splice_command_length = 0;
    SpliceCommandType splice_command_type;
    
    // Payload data based on command type would follow...
    std::vector<uint8_t> payload;
};

struct SpliceInsertCommand {
    uint32_t splice_event_id = 0;
    bool splice_event_cancel_indicator = false;
    bool out_of_network_indicator = false;
    bool program_splice_flag = false;
    bool duration_flag = false;
    bool splice_immediate_flag = false;
    uint64_t pts_time = 0;
    uint64_t break_duration = 0;
    uint16_t unique_program_id = 0;
    uint8_t avail_num = 0;
    uint8_t avails_expected = 0;
};

struct TimeSignalCommand {
    bool time_specified_flag = false;
    uint64_t pts_time = 0;
};

class Scte35Parser {
public:
    static bool Parse(const uint8_t* data, size_t size, Scte35SpliceInfo& outInfo);
    static bool Serialize(const Scte35SpliceInfo& info, std::vector<uint8_t>& outData);
};

} // namespace openmedia::metadata
