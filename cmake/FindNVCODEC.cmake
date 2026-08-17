# FindNVCODEC.cmake
#
# Finds the NVIDIA Video Codec SDK (NVCODEC).
#
# This module defines the following variables:
#   NVCODEC_FOUND       - True if NVCODEC is found.
#   NVCODEC_INCLUDE_DIR - Include directory for NVCODEC headers.
#
# You can set NVCODEC_ROOT_DIR to specify the location of the SDK.

find_path(NVCODEC_INCLUDE_DIR
    NAMES nvcuvid.h nvEncodeAPI.h
    PATHS
        ${NVCODEC_ROOT_DIR}/Interface
        ${NVCODEC_ROOT_DIR}/include
        $ENV{NVCODEC_ROOT_DIR}/Interface
        $ENV{NVCODEC_ROOT_DIR}/include
        "${CMAKE_SOURCE_DIR}/third_party/nvidia_codec_sdk/Interface"
)

include(FindPackageHandleStandardArgs)
find_package_handle_standard_args(NVCODEC
    REQUIRED_VARS NVCODEC_INCLUDE_DIR
)

if(NVCODEC_FOUND)
    set(NVCODEC_INCLUDE_DIRS ${NVCODEC_INCLUDE_DIR})
    if(NOT TARGET NVCODEC::NVCODEC)
        add_library(NVCODEC::NVCODEC INTERFACE IMPORTED)
        set_target_properties(NVCODEC::NVCODEC PROPERTIES
            INTERFACE_INCLUDE_DIRECTORIES "${NVCODEC_INCLUDE_DIR}"
        )
    endif()
endif()

mark_as_advanced(NVCODEC_INCLUDE_DIR)
