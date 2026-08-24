cmake_minimum_required(VERSION 3.14)
project(MediaStats VERSION 1.0.0 LANGUAGES CXX)

# Use C++23 as requested
set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Find SRT
find_package(SRT REQUIRED)

# Try to find WebRTC if possible
# Depending on how WebRTC is integrated, this might differ.
# For now, we will add an option.
option(MEDIASTATS_USE_WEBRTC "Enable WebRTC stats collection" OFF)

add_library(MediaStats
    MediaStats.h
    MediaStats_SRT.cpp
)

target_include_directories(MediaStats PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(MediaStats PRIVATE SRT::srt)

if(MEDIASTATS_USE_WEBRTC)
    target_sources(MediaStats PRIVATE MediaStats_WebRTC.cpp)
    target_compile_definitions(MediaStats PRIVATE OME_HAS_WEBRTC)
    # Assume webrtc is available as a target or via pkg-config
    # target_link_libraries(MediaStats PRIVATE webrtc)
else()
    target_sources(MediaStats PRIVATE MediaStats_WebRTC.cpp) # The dummy impl is inside
endif()
