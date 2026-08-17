cmake_minimum_required(VERSION 3.22)

# OpenMedia.WorkerPool
add_library(OpenMedia.WorkerPool STATIC
    src/WorkerPool.cpp
)

target_include_directories(OpenMedia.WorkerPool
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.WorkerPool
    PUBLIC
        OpenMedia.Core
)

target_compile_features(OpenMedia.WorkerPool PUBLIC cxx_std_23)
