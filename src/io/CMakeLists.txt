cmake_minimum_required(VERSION 3.28)

project(OpenMedia.IO VERSION ${PROJECT_VERSION} LANGUAGES CXX)

# Find FFmpeg using our custom FindFFmpeg.cmake
find_package(FFmpeg REQUIRED)

add_library(OpenMedia.IO
    src/MediaReader.cpp
    src/FileSource.cpp
    src/ImageSource.cpp
    src/LiveSource.cpp
    src/JitterBuffer.cpp
    src/DeviceFactory.cpp
    src/DesktopCapture.cpp
    src/WindowCapture.cpp
    src/DeckLinkSource.cpp
    src/AJASource.cpp
    src/MagewellSource.cpp
    src/DirectShowSource.cpp
    src/WASAPISource.cpp
    src/FileOutput.cpp
    src/SnapshotOutput.cpp
    src/MediaFoundationSource.cpp
    src/NetworkSource.cpp
)

target_include_directories(OpenMedia.IO PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/decklink_sdk/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.IO PUBLIC
    OpenMedia.Core
    FFmpeg::FFmpeg
)

set_target_properties(OpenMedia.IO PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)
