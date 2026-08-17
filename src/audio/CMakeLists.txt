cmake_minimum_required(VERSION 3.28)

project(OpenMedia.Audio VERSION ${PROJECT_VERSION} LANGUAGES CXX)

file(GLOB_RECURSE SRC_FILES "src/*.cpp")
file(GLOB_RECURSE HEADERS "include/openmedia/audio/*.h")

find_package(FFmpeg REQUIRED)
find_package(fmt CONFIG REQUIRED)

add_library(OpenMedia.Audio STATIC ${SRC_FILES} ${HEADERS})

target_include_directories(OpenMedia.Audio
    PUBLIC 
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.Audio
    PUBLIC
        OpenMedia.Core
        FFmpeg::FFmpeg
        fmt::fmt
)

set_target_properties(OpenMedia.Audio PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)
