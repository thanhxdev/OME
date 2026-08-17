#pragma once

#include <string>

// Forward declaration of Lua state to avoid including Lua headers everywhere
struct lua_State;

namespace openmedia {
namespace scripting {

class LuaEngine {
public:
    LuaEngine();
    ~LuaEngine();

    bool Initialize();
    bool ExecuteScript(const std::string& script_content);
    bool ExecuteFile(const std::string& file_path);
    void Shutdown();

private:
    lua_State* L_;
};

} // namespace scripting
} // namespace openmedia
