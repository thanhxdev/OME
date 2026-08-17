cmake_minimum_required(VERSION 3.22)

# OpenMedia.PluginHost — Plugin loading & isolation
add_library(OpenMedia.PluginHost STATIC
    src/PluginHost.cpp
    src/PluginSandbox.cpp
    src/PluginLifecycle.cpp
)

target_include_directories(OpenMedia.PluginHost
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.PluginHost
    PUBLIC
        OpenMedia.Core
)

if(WIN32)
    target_link_libraries(OpenMedia.PluginHost PRIVATE Kernel32)
endif()

target_compile_features(OpenMedia.PluginHost PUBLIC cxx_std_23)

add_executable(ome_plugin_host
    src/main.cpp
)

target_link_libraries(ome_plugin_host
    PRIVATE
        OpenMedia.Core
        OpenMedia.IPC
        OpenMedia.PluginHost
        OpenMedia.PluginSDK
)

# Output executable to dist/bin
set_target_properties(ome_plugin_host PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${PROJECT_SOURCE_DIR}/dist/bin
)
