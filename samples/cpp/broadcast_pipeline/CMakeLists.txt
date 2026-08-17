# ============================================================
# Broadcast Pipeline Sample - CMakeLists.txt
# ============================================================

add_executable(broadcast_pipeline main.cpp)

target_link_libraries(broadcast_pipeline
    PRIVATE
        OpenMedia.Core
        OpenMedia.IO
        OpenMedia.Mixer
        OpenMedia.Overlay
        OpenMedia.Codecs
        # Ensure your custom protocol libraries exist, e.g. OpenMedia.SRT, OpenMedia.RTMP
        # If they use different names, adjust accordingly. We'll use dummy fallbacks in main.cpp
        # if the real ones are not available.
)

set_target_properties(broadcast_pipeline PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)
