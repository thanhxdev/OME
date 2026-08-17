#pragma once

#include <string>
#include <regex>
#include <map>
#include <sstream>

namespace openmedia::srt {

enum class SRTMode {
    Caller,
    Listener
};

struct SRTUriConfig {
    std::string ip;
    int port = 0;
    SRTMode mode = SRTMode::Caller;
    std::string passphrase;
    int pbkeylen = 16;
    int latency = 120;
    int maxbw = -1; // -1 means infinite/default

    static std::map<std::string, std::string> ParseQuery(const std::string& query) {
        std::map<std::string, std::string> params;
        std::istringstream stream(query);
        std::string pair;
        while (std::getline(stream, pair, '&')) {
            auto pos = pair.find('=');
            if (pos != std::string::npos) {
                params[pair.substr(0, pos)] = pair.substr(pos + 1);
            }
        }
        return params;
    }

    // srt://<ip>:<port>?mode=<caller|listener>&passphrase=<pass>&pbkeylen=<16|24|32>&latency=<ms>&maxbw=<bps>
    static bool Parse(const std::string& uri, SRTUriConfig& config) {
        std::regex uri_regex(R"(srt://([^:]+):(\d+)(?:\?(.*))?)");
        std::smatch match;
        
        if (std::regex_match(uri, match, uri_regex)) {
            config.ip = match[1].str();
            config.port = std::stoi(match[2].str());
            
            if (match.size() > 3 && match[3].length() > 0) {
                std::string query = match[3].str();
                auto params = ParseQuery(query);

                if (params["mode"] == "listener") {
                    config.mode = SRTMode::Listener;
                } else {
                    config.mode = SRTMode::Caller;
                }

                if (params.count("passphrase")) {
                    config.passphrase = params["passphrase"];
                }
                
                if (params.count("pbkeylen")) {
                    config.pbkeylen = std::stoi(params["pbkeylen"]);
                }

                if (params.count("latency")) {
                    config.latency = std::stoi(params["latency"]);
                }

                if (params.count("maxbw")) {
                    config.maxbw = std::stoi(params["maxbw"]);
                }
            } else {
                config.mode = SRTMode::Caller; // Default
            }
            return true;
        }
        return false;
    }
};

} // namespace openmedia::srt
