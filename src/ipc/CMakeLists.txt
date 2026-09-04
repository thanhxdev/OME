cmake_minimum_required(VERSION 3.28)

# OpenMedia.IPC — IPC Transport Layer
add_library(OpenMedia.IPC STATIC
    src/NamedPipeServer.cpp
    src/NamedPipeClient.cpp
    src/SharedMemoryBuffer.cpp
    src/D3D11SharedTexture.cpp
    src/D3D11SharedTexturePoolPoC.cpp
    src/IPCServer.cpp
    src/IPCClient.cpp
)

target_include_directories(OpenMedia.IPC
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.IPC
    PUBLIC
        OpenMedia.Core
)

# Platform-specific dependencies
if(WIN32)
    target_link_libraries(OpenMedia.IPC PRIVATE Kernel32 d3d11 dxgi)
endif()

target_compile_features(OpenMedia.IPC PUBLIC cxx_std_23)
