# ============================================================
# OpenMedia.Core — CMakeLists.txt
# ============================================================
add_library(OpenMedia.Core STATIC
    src/Logger.cpp
    src/Config.cpp
    src/MemoryPool.cpp
    src/MediaFrame.cpp
    src/FrameQueue.cpp
    src/ClockSync.cpp
    src/MediaPipeline.cpp
    src/Engine.cpp
)

target_include_directories(OpenMedia.Core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
        ${CMAKE_BINARY_DIR}/generated
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

target_compile_definitions(OpenMedia.Core
    PRIVATE
        OME_CORE_EXPORTS
)

set_target_properties(OpenMedia.Core PROPERTIES
    VERSION ${PROJECT_VERSION}
    OUTPUT_NAME "OpenMedia.Core"
    CXX_STANDARD 20
    CXX_STANDARD_REQUIRED ON
)

# Include generated config header
target_include_directories(OpenMedia.Core
    PRIVATE
        ${CMAKE_BINARY_DIR}/generated
)
