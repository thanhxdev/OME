cmake_minimum_required(VERSION 3.28)

project(OpenMedia.Codecs VERSION ${PROJECT_VERSION} LANGUAGES CXX)

# Find FFmpeg
find_package(FFmpeg REQUIRED)

# Find NVCODEC and CUDA Toolkit
find_package(NVCODEC)
find_package(CUDAToolkit)

add_library(OpenMedia.Codecs
    src/CodecFactory.cpp
    src/FFmpegH264Encoder.cpp
    src/FFmpegH264Decoder.cpp
    src/FFmpegH265Encoder.cpp
    src/FFmpegH265Decoder.cpp
    src/FFmpegNVENCEncoder.cpp
    src/FFmpegNVDECDecoder.cpp
    src/FFmpegQSVEncoder.cpp
    src/FFmpegQSVDecoder.cpp
    src/FFmpegAV1Encoder.cpp
    src/FFmpegAACEncoder.cpp
    src/FFmpegOpusEncoder.cpp
    src/NVENCEncoder.cpp
    src/H264Decoder_NV.cpp
)

target_include_directories(OpenMedia.Codecs PUBLIC
    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
    $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMedia.Codecs PUBLIC
    OpenMedia.Core
    FFmpeg::FFmpeg
)

if(NVCODEC_FOUND AND CUDAToolkit_FOUND)
    target_link_libraries(OpenMedia.Codecs PRIVATE 
        NVCODEC::NVCODEC 
        CUDA::cuda_driver 
        CUDA::cudart
    )
    target_compile_definitions(OpenMedia.Codecs PRIVATE HAS_NVCODEC)
endif()

set_target_properties(OpenMedia.Codecs PROPERTIES
    CXX_STANDARD 23
    CXX_STANDARD_REQUIRED ON
)
