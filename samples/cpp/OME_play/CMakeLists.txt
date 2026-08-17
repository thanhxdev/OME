cmake_minimum_required(VERSION 3.28)

project(OME_play LANGUAGES CXX)

add_executable(OME_play
    main.cpp
)

target_link_libraries(OME_play PRIVATE
    OpenMedia.Core
    OpenMedia.IO
    OpenMedia.Codecs
    OpenMedia.Rendering
    OpenMedia.Audio
)

set_target_properties(OME_play PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)
