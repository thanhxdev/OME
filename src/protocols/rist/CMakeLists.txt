cmake_minimum_required(VERSION 3.15)
project(OpenMediaRIST)

set(CMAKE_CXX_STANDARD 20)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMediaRIST STATIC ${SOURCES})

target_include_directories(OpenMediaRIST PUBLIC include)

# target_link_libraries(OpenMediaRIST PUBLIC OpenMediaCore ...)
