cmake_minimum_required(VERSION 3.28)
project(OpenMediaST2022)

set(CMAKE_CXX_STANDARD 20)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMediaST2022 STATIC ${SOURCES})

target_include_directories(OpenMediaST2022 PUBLIC include)

# TODO: Link DPDK, Rivermax, or other backend dependencies
# target_link_libraries(OpenMediaST2022 PUBLIC OpenMediaCore ...)
