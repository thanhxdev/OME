# ============================================================
# Dependencies.cmake
# Third-party dependency management
# ============================================================

list(APPEND CMAKE_PREFIX_PATH "${CMAKE_SOURCE_DIR}/third_party")

# --- Core Dependencies (vcpkg managed) ---
# These will be available once vcpkg install is run

# spdlog — structured logging
find_package(spdlog CONFIG QUIET)
if(spdlog_FOUND)
    message(STATUS "Found spdlog: ${spdlog_VERSION}")
else()
    message(STATUS "spdlog not found — will use header-only fallback or vcpkg install required")
endif()

# nlohmann_json — JSON handling
find_package(nlohmann_json CONFIG QUIET)
if(nlohmann_json_FOUND)
    message(STATUS "Found nlohmann_json: ${nlohmann_json_VERSION}")
else()
    message(STATUS "nlohmann_json not found — vcpkg install required")
endif()

# fmt — string formatting
find_package(fmt CONFIG QUIET)
if(fmt_FOUND)
    message(STATUS "Found fmt: ${fmt_VERSION}")
else()
    message(STATUS "fmt not found — vcpkg install required")
endif()

# Google Test — unit testing
if(OME_BUILD_TESTS)
    find_package(GTest CONFIG QUIET)
    if(GTest_FOUND)
        message(STATUS "Found GTest: ${GTest_VERSION}")
    else()
        message(STATUS "GTest not found — vcpkg install required")
    endif()
endif()

# --- FFmpeg (vendored or system) ---
# find_package(FFmpeg COMPONENTS avformat avcodec avutil swscale swresample QUIET)
# if(FFmpeg_FOUND)
#     message(STATUS "Found FFmpeg")
# endif()

# --- Vendored Third-Party Paths ---
set(THIRD_PARTY_DIR "${CMAKE_SOURCE_DIR}/third_party")
set(FFMPEG_DIR "${THIRD_PARTY_DIR}/ffmpeg")
set(NDI_SDK_DIR "${THIRD_PARTY_DIR}/ndi_sdk")
set(DECKLINK_SDK_DIR "${THIRD_PARTY_DIR}/decklink_sdk")
set(AJA_SDK_DIR "${THIRD_PARTY_DIR}/aja_sdk")
set(MAGEWELL_SDK_DIR "${THIRD_PARTY_DIR}/magewell_sdk")
set(CEF_DIR "${THIRD_PARTY_DIR}/cef")
set(NVIDIA_CODEC_SDK_DIR "${THIRD_PARTY_DIR}/nvidia_codec_sdk")
set(INTEL_ONEVPL_DIR "${THIRD_PARTY_DIR}/intel_onevpl")
