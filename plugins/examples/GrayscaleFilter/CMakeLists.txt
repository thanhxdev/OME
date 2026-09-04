cmake_minimum_required(VERSION 3.28)
project(GrayscaleFilterPlugin VERSION 1.0.0)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED True)

add_library(GrayscaleFilterPlugin SHARED
    GrayscaleFilterPlugin.cpp
)

target_include_directories(GrayscaleFilterPlugin
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
)

target_link_libraries(GrayscaleFilterPlugin
    PRIVATE
        OpenMedia.Core
        OpenMedia.PluginSDK
)

# Output to dist/plugins folder
set_target_properties(GrayscaleFilterPlugin PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${PROJECT_SOURCE_DIR}/dist/plugins
    LIBRARY_OUTPUT_DIRECTORY ${PROJECT_SOURCE_DIR}/dist/plugins
)
