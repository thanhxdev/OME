cmake_minimum_required(VERSION 3.15)
project(OpenMediaOutputsOTT)

set(CMAKE_CXX_STANDARD 20)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMediaOutputsOTT STATIC ${SOURCES})

target_include_directories(OpenMediaOutputsOTT PUBLIC include)

# TODO: Link FFmpeg libraries (libavformat, libavcodec, libavutil)
# target_link_libraries(OpenMediaOutputsOTT PUBLIC OpenMediaCore ...)
