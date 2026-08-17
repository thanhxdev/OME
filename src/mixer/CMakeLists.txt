cmake_minimum_required(VERSION 3.28)

project(OpenMedia.Mixer)

file(GLOB_RECURSE SRC_FILES "src/*.cpp")
file(GLOB_RECURSE HEADERS "include/openmedia/mixer/*.h")

add_library(OpenMedia.Mixer
    src/MixerLayer.cpp
    src/Mixer.cpp
    src/Transition.cpp
    src/Switcher.cpp
    src/ChromaKey.cpp
    src/LumaKey.cpp
    src/CropFilter.cpp
    src/ScaleFilter.cpp
    src/LUTFilter.cpp
)

target_include_directories(OpenMedia.Mixer PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

# Find FFmpeg if not already provided by OpenMedia.Core (we need it directly for swscale)
find_package(FFmpeg REQUIRED)

target_link_libraries(OpenMedia.Mixer PUBLIC
    OpenMedia.Core
    OpenMedia.GPU
    FFmpeg::FFmpeg
)
