cmake_minimum_required(VERSION 3.28)
project(OpenMedia.PluginSDK)

set(CMAKE_CXX_STANDARD 23)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMedia.PluginSDK SHARED ${SOURCES})

target_include_directories(OpenMedia.PluginSDK PUBLIC 
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.PluginSDK PRIVATE OpenMedia.Core OpenMedia.IPC)

set_target_properties(OpenMedia.PluginSDK PROPERTIES
    OUTPUT_NAME "OpenMedia.PluginSDK"
    CXX_STANDARD 23
    WINDOWS_EXPORT_ALL_SYMBOLS ON
)
