cmake_minimum_required(VERSION 3.28)

project(OpenMedia.Playlist)

file(GLOB_RECURSE SRC_FILES "src/*.cpp")
file(GLOB_RECURSE HEADERS "include/openmedia/playlist/*.h")

add_library(OpenMedia.Playlist STATIC ${SRC_FILES} ${HEADERS})

target_include_directories(OpenMedia.Playlist
    PUBLIC 
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.Playlist
    PUBLIC
        OpenMedia.Core
)
