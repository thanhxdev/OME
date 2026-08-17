#pragma once

struct lua_State;

namespace openmedia {
namespace scripting {

class LuaBindings {
public:
    static void BindCore(lua_State* L);
};

} // namespace scripting
} // namespace openmedia
