cmake_minimum_required(VERSION 3.28)
project(OpenMediaPlugins)

set(CMAKE_CXX_STANDARD 20)

include_directories(
    include
    ${CMAKE_SOURCE_DIR}/src/core/include
    ${CMAKE_SOURCE_DIR}/src/plugin_sdk/include
)

# GrayscaleFilter Plugin
add_library(GrayscaleFilter SHARED src/GrayscaleFilter.cpp)
target_include_directories(GrayscaleFilter PUBLIC include)
set_target_properties(GrayscaleFilter PROPERTIES PREFIX "")

# GainFilter Plugin
add_library(GainFilter SHARED src/GainFilter.cpp)
target_include_directories(GainFilter PUBLIC include)
set_target_properties(GainFilter PROPERTIES PREFIX "")

# Copy to plugins output dir if needed
# set_target_properties(GrayscaleFilter PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/plugins")
# set_target_properties(GainFilter PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/plugins")

# ColorCorrectionFilter Plugin
add_library(ColorCorrectionFilter SHARED src/ColorCorrectionFilter.cpp)
target_include_directories(ColorCorrectionFilter PUBLIC include)
set_target_properties(ColorCorrectionFilter PROPERTIES PREFIX "")
target_link_libraries(ColorCorrectionFilter PRIVATE fmt::fmt)

# LUTFilter Plugin
add_library(LUTFilter SHARED src/LUTFilter.cpp)
target_include_directories(LUTFilter PUBLIC include)
set_target_properties(LUTFilter PROPERTIES PREFIX "")
target_link_libraries(LUTFilter PRIVATE fmt::fmt)
