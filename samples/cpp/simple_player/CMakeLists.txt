cmake_minimum_required(VERSION 3.28)

project(simple_player LANGUAGES CXX)

add_executable(simple_player
    main.cpp
)

target_link_libraries(simple_player PRIVATE
    OpenMedia.Core
    OpenMedia.IO
    OpenMedia.Codecs
    OpenMedia.Rendering
    OpenMedia.Audio
)

set_target_properties(simple_player PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)
