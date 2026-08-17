cmake_minimum_required(VERSION 3.15)
project(OpenMediaST2110)

set(CMAKE_CXX_STANDARD 20)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMediaST2110 STATIC ${SOURCES})

target_include_directories(OpenMediaST2110 PUBLIC include)

# TODO: Link DPDK, Rivermax, or other backend dependencies
# target_link_libraries(OpenMediaST2110 PUBLIC OpenMediaCore ...)
