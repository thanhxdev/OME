cmake_minimum_required(VERSION 3.28)
project(OpenMediaOutputsDecklink)

set(CMAKE_CXX_STANDARD 20)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMediaOutputsDecklink STATIC ${SOURCES})

target_include_directories(OpenMediaOutputsDecklink PUBLIC 
    include
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/../../../third_party/decklink_sdk/include>
)

target_link_libraries(OpenMediaOutputsDecklink PUBLIC OpenMedia.Core)
