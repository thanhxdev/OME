cmake_minimum_required(VERSION 3.28)

project(OpenMedia.Rendering)

file(GLOB_RECURSE SRC_FILES "src/*.cpp")
file(GLOB_RECURSE HEADERS "include/openmedia/rendering/*.h")

add_library(OpenMedia.Rendering STATIC ${SRC_FILES} ${HEADERS})

target_include_directories(OpenMedia.Rendering
    PUBLIC 
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.Rendering
    PUBLIC
        OpenMedia.Core
        OpenMedia.GPU
        d3d11
        dxgi
        d3dcompiler
)
