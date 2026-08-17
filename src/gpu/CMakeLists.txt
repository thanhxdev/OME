cmake_minimum_required(VERSION 3.20)

project(OpenMedia.GPU)

file(GLOB_RECURSE SRC_FILES "src/*.cpp")
file(GLOB_RECURSE INC_FILES "include/*.h")

add_library(OpenMedia.GPU STATIC ${SRC_FILES} ${INC_FILES})

target_include_directories(OpenMedia.GPU PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.GPU PUBLIC OpenMedia.Core)

# Link D3D11 and DXGI for Windows
if(WIN32)
    target_link_libraries(OpenMedia.GPU PRIVATE d3d11.lib dxgi.lib)
endif()

# Find and Link CUDA if available
find_package(CUDAToolkit QUIET)
if(CUDAToolkit_FOUND)
    target_compile_definitions(OpenMedia.GPU PRIVATE OME_HAS_CUDA)
    target_link_libraries(OpenMedia.GPU PRIVATE CUDA::cuda_driver CUDA::cudart)
endif()

# Find NVCODEC SDK for NVENC/NVDEC header access
find_package(NVCODEC QUIET)
if(NVCODEC_FOUND)
    target_link_libraries(OpenMedia.GPU PRIVATE NVCODEC::NVCODEC)
endif()

set_target_properties(OpenMedia.GPU PROPERTIES CXX_STANDARD 23)

# Copy shaders to the binary directory so tests and apps can find them at runtime
add_custom_command(TARGET OpenMedia.GPU POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
    "${CMAKE_CURRENT_SOURCE_DIR}/shaders"
    "${CMAKE_BINARY_DIR}/shaders"
)
