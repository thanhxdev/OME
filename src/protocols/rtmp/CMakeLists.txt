cmake_minimum_required(VERSION 3.20)
project(OpenMedia.RTMP)

file(GLOB_RECURSE SRC_FILES "src/*.cpp")
file(GLOB_RECURSE HEADER_FILES "include/*.h")

add_library(OpenMedia.RTMP STATIC ${SRC_FILES} ${HEADER_FILES})

target_include_directories(OpenMedia.RTMP
    PUBLIC 
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.RTMP
    PUBLIC 
        OpenMedia.Core
        OpenMedia.IO
)

