# ============================================================
# Mixer Demo Sample - CMakeLists.txt
# ============================================================

add_executable(mixer_demo main.cpp)

target_link_libraries(mixer_demo
    PRIVATE
        OpenMedia.Core
        OpenMedia.IO
        OpenMedia.Mixer
        OpenMedia.Rendering
)

set_target_properties(mixer_demo PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)
