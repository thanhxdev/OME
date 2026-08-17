#include "openmedia/scripting/LuaBindings.h"

namespace openmedia {
namespace scripting {

void LuaBindings::BindCore(lua_State* L) {
    if (!L) return;
    
    // TODO: Register Pipeline class
    // sol::state_view lua(L);
    // lua.new_usertype<Pipeline>("Pipeline",
    //     "AddNode", &Pipeline::AddNode,
    //     "Connect", &Pipeline::Connect,
    //     "Start", &Pipeline::Start,
    //     "Stop", &Pipeline::Stop
    // );
    
    // TODO: Register Nodes (FileSource, SRTOutput, etc.)
}

} // namespace scripting
} // namespace openmedia
