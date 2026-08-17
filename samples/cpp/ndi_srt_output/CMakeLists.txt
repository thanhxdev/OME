cmake_minimum_required(VERSION 3.28)

project(ndi_srt_output LANGUAGES CXX)

add_executable(ndi_srt_output
    main.cpp
)

target_link_libraries(ndi_srt_output PRIVATE
    OpenMedia.Core
    OpenMedia.Codecs
    OpenMedia_NDI
    OpenMedia_SRT
)

set_target_properties(ndi_srt_output PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)
