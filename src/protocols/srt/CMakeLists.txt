cmake_minimum_required(VERSION 3.28)

project(OpenMedia_SRT)

set(SOURCES
    src/SRTEngine.cpp
    src/SRTSource.cpp
    src/SRTOutput.cpp
)

set(HEADERS
    include/openmedia/srt/SRTEngine.h
    include/openmedia/srt/SRTSource.h
    include/openmedia/srt/SRTOutput.h
)

find_path(SRT_INCLUDE_DIR NAMES srt/srt.h)
find_library(SRT_LIBRARY NAMES srt srt_static)

if(NOT SRT_INCLUDE_DIR OR NOT SRT_LIBRARY)
    message(FATAL_ERROR "SRT library not found!")
endif()

add_library(OpenMedia_SRT STATIC ${SOURCES} ${HEADERS})

target_include_directories(OpenMedia_SRT
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia_SRT
    PUBLIC
        OpenMedia.Core
    PRIVATE
        ${SRT_LIBRARY}
        ws2_32.lib
)

target_include_directories(OpenMedia_SRT
    PRIVATE
        ${SRT_INCLUDE_DIR}
)

