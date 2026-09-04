cmake_minimum_required(VERSION 3.28)
project(LowerThirdOverlayPlugin VERSION 1.0.0)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED True)

add_library(LowerThirdOverlayPlugin SHARED
    LowerThirdOverlayPlugin.cpp
)

target_include_directories(LowerThirdOverlayPlugin
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}
)

target_link_libraries(LowerThirdOverlayPlugin
    PRIVATE
        OpenMedia.Core
        OpenMedia.PluginSDK
)

# Output to dist/plugins folder
set_target_properties(LowerThirdOverlayPlugin PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY ${PROJECT_SOURCE_DIR}/dist/plugins
    LIBRARY_OUTPUT_DIRECTORY ${PROJECT_SOURCE_DIR}/dist/plugins
)
