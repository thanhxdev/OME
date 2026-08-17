# FindFFmpeg.cmake
# Find the FFmpeg libraries
#
# This module defines:
# FFMPEG_FOUND - true if FFmpeg was found
# FFMPEG_INCLUDE_DIRS - the directory containing FFmpeg headers
# FFMPEG_LIBRARIES - the libraries to link against
#
# Also defines imported targets:
# FFmpeg::FFmpeg

find_path(FFMPEG_INCLUDE_DIR
    NAMES libavcodec/avcodec.h
    PATHS
    ${CMAKE_SOURCE_DIR}/third_party/ffmpeg/include
    ${FFMPEG_ROOT}/include
)

set(FFMPEG_LIBS
    avcodec avformat avutil avdevice avfilter swscale swresample
)

set(FFMPEG_LIBRARIES "")
foreach(_lib ${FFMPEG_LIBS})
    find_library(FFMPEG_${_lib}_LIBRARY
        NAMES ${_lib}
        PATHS
        ${CMAKE_SOURCE_DIR}/third_party/ffmpeg/lib/x64-windows
        ${FFMPEG_ROOT}/lib
    )
    if(FFMPEG_${_lib}_LIBRARY)
        list(APPEND FFMPEG_LIBRARIES ${FFMPEG_${_lib}_LIBRARY})
    endif()
endforeach()

include(FindPackageHandleStandardArgs)
find_package_handle_standard_args(FFmpeg
    REQUIRED_VARS FFMPEG_INCLUDE_DIR FFMPEG_LIBRARIES
)

if(FFMPEG_FOUND AND NOT TARGET FFmpeg::FFmpeg)
    add_library(FFmpeg::FFmpeg INTERFACE IMPORTED)
    set_target_properties(FFmpeg::FFmpeg PROPERTIES
        INTERFACE_INCLUDE_DIRECTORIES "${FFMPEG_INCLUDE_DIR}"
        INTERFACE_LINK_LIBRARIES "${FFMPEG_LIBRARIES}"
    )
endif()

mark_as_advanced(FFMPEG_INCLUDE_DIR)
foreach(_lib ${FFMPEG_LIBS})
    mark_as_advanced(FFMPEG_${_lib}_LIBRARY)
endforeach()
