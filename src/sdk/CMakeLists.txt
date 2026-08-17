cmake_minimum_required(VERSION 3.22)

# OpenMedia.SDK — Client Library (SDK Proxy)
add_library(OpenMedia.SDK STATIC
    src/SDKEngine.cpp
    src/SDKPipeline.cpp
    src/SDKSource.cpp
)

target_include_directories(OpenMedia.SDK
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.SDK
    PUBLIC
        OpenMedia.Core
        OpenMedia.IPC
)

if(WIN32)
    target_link_libraries(OpenMedia.SDK PRIVATE Kernel32)
endif()

target_compile_features(OpenMedia.SDK PUBLIC cxx_std_23)
