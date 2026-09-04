cmake_minimum_required(VERSION 3.28)
project(OpenMediaScripting)

set(CMAKE_CXX_STANDARD 20)

include_directories(include)

file(GLOB SOURCES "src/*.cpp")

add_library(OpenMediaScripting STATIC ${SOURCES})

target_include_directories(OpenMediaScripting PUBLIC include)

# target_link_libraries(OpenMediaScripting PUBLIC OpenMediaCore lua)
