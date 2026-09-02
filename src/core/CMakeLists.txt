# ============================================================
# OpenMedia.Core — CMakeLists.txt
# ============================================================
add_library(OpenMedia.Core SHARED
    src/Logger.cpp
    src/Config.cpp
    src/MemoryPool.cpp
    src/MediaMetadata.cpp
    src/MediaFrame.cpp
    src/FrameQueue.cpp
    src/ClockSync.cpp
    src/AVSyncClock.cpp
    src/MediaPipeline.cpp
    src/PipelineGraph.cpp
    src/TeeNode.cpp
    src/Engine.cpp
    openmedia_c_api.cpp
)

target_include_directories(OpenMedia.Core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
        ${CMAKE_BINARY_DIR}/generated
        ${CMAKE_SOURCE_DIR}/src/protocols/srt/include
        ${CMAKE_SOURCE_DIR}/src/mixer/include
        ${CMAKE_SOURCE_DIR}/src/audio/include
        ${CMAKE_SOURCE_DIR}/src/overlay/include
)

# Link dependencies (conditional on availability)
if(spdlog_FOUND)
    target_link_libraries(OpenMedia.Core PUBLIC spdlog::spdlog)
else()
    # Fallback: build without spdlog (stubs)
    target_compile_definitions(OpenMedia.Core PRIVATE OME_NO_SPDLOG=1)
endif()

if(nlohmann_json_FOUND)
    target_link_libraries(OpenMedia.Core PUBLIC nlohmann_json::nlohmann_json)
endif()

if(fmt_FOUND)
    target_link_libraries(OpenMedia.Core PUBLIC fmt::fmt)
endif()

find_path(SRT_INCLUDE_DIR NAMES srt/srt.h)
find_library(SRT_LIBRARY NAMES srt srt_static)
if(SRT_INCLUDE_DIR AND SRT_LIBRARY)
    target_sources(OpenMedia.Core PRIVATE
        ${CMAKE_SOURCE_DIR}/src/protocols/srt/src/SRTEngine.cpp
        ${CMAKE_SOURCE_DIR}/src/protocols/srt/src/SRTSource.cpp
        ${CMAKE_SOURCE_DIR}/src/protocols/srt/src/SRTOutput.cpp
        ${CMAKE_SOURCE_DIR}/src/audio/src/AudioMeter.cpp
    )
    target_include_directories(OpenMedia.Core PRIVATE ${SRT_INCLUDE_DIR})
    target_link_libraries(OpenMedia.Core PRIVATE ${SRT_LIBRARY} ws2_32.lib)
else()
    target_sources(OpenMedia.Core PRIVATE
        ${CMAKE_SOURCE_DIR}/src/audio/src/AudioMeter.cpp
    )
endif()

target_compile_definitions(OpenMedia.Core
    PRIVATE
        OME_CORE_EXPORTS
)

set_target_properties(OpenMedia.Core PROPERTIES
    VERSION ${PROJECT_VERSION}
    OUTPUT_NAME "OpenMedia.Core"
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
    WINDOWS_EXPORT_ALL_SYMBOLS ON
)

# Include generated config header
target_include_directories(OpenMedia.Core
    PRIVATE
        ${CMAKE_BINARY_DIR}/generated
)
