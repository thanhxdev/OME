cmake_minimum_required(VERSION 3.15)
project(OpenMediaOutputsIPC)

set(CMAKE_CXX_STANDARD 20)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMediaOutputsIPC STATIC ${SOURCES})

target_include_directories(OpenMediaOutputsIPC PUBLIC include)

# target_link_libraries(OpenMediaOutputsIPC PUBLIC OpenMediaCore Boost::interprocess)
