cmake_minimum_required(VERSION 3.28)

# Assuming benchmark is available either via vcpkg or FetchContent
# find_package(benchmark REQUIRED)

add_executable(test_pipeline_performance test_pipeline_performance.cpp)

target_link_libraries(test_pipeline_performance PRIVATE 
    OpenMedia.Core
    OpenMedia.IO
    OpenMedia.Codecs
    OpenMedia.Audio
    OpenMedia.Rendering
    OpenMedia.Overlay
    OpenMedia.Mixer
    OpenMedia.CGEngine
    benchmark::benchmark
)

set_target_properties(test_pipeline_performance PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)

add_test(NAME test_pipeline_performance COMMAND test_pipeline_performance)
