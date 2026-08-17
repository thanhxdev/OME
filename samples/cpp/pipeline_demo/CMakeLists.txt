cmake_minimum_required(VERSION 3.28)

project(pipeline_demo)

add_executable(pipeline_demo main.cpp)

target_link_libraries(pipeline_demo
    PRIVATE
        OpenMedia.Core
        OpenMedia.IO
        OpenMedia.Mixer
        OpenMedia.Rendering
        OpenMedia.Audio
)
