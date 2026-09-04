cmake_minimum_required(VERSION 3.28)
project(OpenMediaMonitoring)

set(CMAKE_CXX_STANDARD 23)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMedia.Monitoring STATIC ${SOURCES})

target_include_directories(OpenMedia.Monitoring PUBLIC include)
target_link_libraries(OpenMedia.Monitoring PRIVATE OpenMedia.Core)
