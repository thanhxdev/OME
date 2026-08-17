using System;

namespace OpenMedia.SDK
{
    public class ScriptEngine
    {
        public ScriptEngine()
        {
        }

        public bool RunScript(string scriptContent)
        {
            return NativeBridge.om_run_lua_script(scriptContent);
        }
    }
}
