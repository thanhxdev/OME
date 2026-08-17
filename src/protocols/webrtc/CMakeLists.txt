cmake_minimum_required(VERSION 3.20)

project(OpenMedia_WebRTC)

set(SOURCES
    src/WebRTCEngine.cpp
    src/WebRTCSource.cpp
    src/WebRTCOutput.cpp
)

set(HEADERS
    include/openmedia/webrtc/WebRTCEngine.h
    include/openmedia/webrtc/WebRTCSource.h
    include/openmedia/webrtc/WebRTCOutput.h
)

add_library(OpenMedia_WebRTC STATIC ${SOURCES} ${HEADERS})

target_include_directories(OpenMedia_WebRTC
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia_WebRTC
    PUBLIC
        OpenMedia.Core
)

# TODO: Add find_package(webrtc) and link against it
