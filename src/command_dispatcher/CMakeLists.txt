cmake_minimum_required(VERSION 3.22)

# OpenMedia.CommandDispatcher
add_library(OpenMedia.CommandDispatcher STATIC
    src/CommandDispatcher.cpp
)

target_include_directories(OpenMedia.CommandDispatcher
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.CommandDispatcher
    PUBLIC
        OpenMedia.Core
        OpenMedia.IPC
)

target_compile_features(OpenMedia.CommandDispatcher PUBLIC cxx_std_23)
