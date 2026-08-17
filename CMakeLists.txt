# ============================================================
# OpenMedia SDK — Root CMakeLists.txt
# ============================================================
cmake_minimum_required(VERSION 3.28)

# --- vcpkg Integration ---
if(DEFINED ENV{VCPKG_ROOT})
    set(CMAKE_TOOLCHAIN_FILE "$ENV{VCPKG_ROOT}/scripts/buildsystems/vcpkg.cmake"
        CACHE STRING "Vcpkg toolchain file")
else()
    set(CMAKE_TOOLCHAIN_FILE "${CMAKE_SOURCE_DIR}/third_party/vcpkg/scripts/buildsystems/vcpkg.cmake"
        CACHE STRING "Vcpkg toolchain file")
endif()

project(OpenMediaSDK
    VERSION 1.0.0
    DESCRIPTION "High-Performance Media Engine SDK"
    LANGUAGES CXX C
)

# --- C++23 Standard (std::expected requires C++23) ---
set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# --- Global Settings ---
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_WINDOWS_EXPORT_ALL_SYMBOLS OFF)

# --- Output Directories ---
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

# --- Include CMake Modules ---
list(APPEND CMAKE_MODULE_PATH "${CMAKE_SOURCE_DIR}/cmake")
include(CompilerSettings)
include(Dependencies)
include(Platform)
include(Version)
include(EnvironmentConfig)

# --- Options ---
option(OME_BUILD_TESTS      "Build unit tests"         ON)
option(OME_BUILD_BENCHMARKS "Build benchmarks"         OFF)
option(OME_BUILD_SAMPLES    "Build sample apps"        ON)
option(OME_BUILD_PLUGINS    "Build built-in plugins"   ON)
option(OME_BUILD_DOCS       "Build documentation"      OFF)

option(OME_ENABLE_GPU       "Enable GPU acceleration"  ON)
option(OME_ENABLE_NDI       "Enable NDI support"       ON)
option(OME_ENABLE_SRT       "Enable SRT support"       ON)
option(OME_ENABLE_WEBRTC    "Enable WebRTC support"    ON)
option(OME_ENABLE_ST2110    "Enable ST2110 support"    OFF)
option(OME_ENABLE_ST2022    "Enable ST2022 support"    OFF)
option(OME_ENABLE_CEF       "Enable CEF HTML overlay"  ON)

# --- Core Modules (always built) ---
add_subdirectory(src/core)
add_subdirectory(src/io)
add_subdirectory(src/codecs)
add_subdirectory(src/rendering)
add_subdirectory(src/mixer)
add_subdirectory(src/audio)
add_subdirectory(src/overlay)
add_subdirectory(src/cg)
add_subdirectory(src/playlist)
add_subdirectory(src/monitoring)
add_subdirectory(src/plugin_sdk)

# --- Exhand Architecture Modules ---
add_subdirectory(src/ipc)
add_subdirectory(src/command_dispatcher)
add_subdirectory(src/worker_pool)
add_subdirectory(src/plugin_host)
add_subdirectory(src/sdk)
add_subdirectory(src/server)

# --- Optional Modules ---
if(OME_ENABLE_GPU)
    add_subdirectory(src/gpu)
endif()

# --- Protocol Engines ---
add_subdirectory(src/protocols/rtmp)
if(OME_ENABLE_SRT)
    add_subdirectory(src/protocols/srt)
endif()
if(OME_ENABLE_NDI)
    add_subdirectory(src/protocols/ndi)
endif()
if(OME_ENABLE_WEBRTC)
    add_subdirectory(src/protocols/webrtc)
endif()
if(OME_ENABLE_ST2110)
    add_subdirectory(src/protocols/st2110)
endif()
if(OME_ENABLE_ST2022)
    add_subdirectory(src/protocols/st2022)
endif()

# --- Tests ---
if(OME_BUILD_TESTS)
    enable_testing()
    add_subdirectory(tests)
endif()

# --- Samples ---
if(OME_BUILD_SAMPLES)
    add_subdirectory(samples/cpp)
endif()

# --- Plugins ---
if(OME_BUILD_PLUGINS)
    add_subdirectory(plugins)
endif()

# --- Install Rules ---
include(GNUInstallDirs)
install(DIRECTORY src/core/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/io/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/codecs/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/rendering/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/mixer/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/audio/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/overlay/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/cg/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/playlist/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/monitoring/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/plugin_sdk/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/ipc/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(DIRECTORY src/sdk/include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

# --- Summary ---
message(STATUS "")
message(STATUS "╔══════════════════════════════════════════════════╗")
message(STATUS "║         OpenMedia SDK Build Configuration        ║")
message(STATUS "╠══════════════════════════════════════════════════╣")
message(STATUS "║  Version:      ${PROJECT_VERSION}")
message(STATUS "║  Environment:  ${OME_ENV_TAG}")
message(STATUS "║  Build Type:   ${CMAKE_BUILD_TYPE}")
message(STATUS "║  Compiler:     ${CMAKE_CXX_COMPILER_ID} ${CMAKE_CXX_COMPILER_VERSION}")
message(STATUS "║  C++ Standard: ${CMAKE_CXX_STANDARD}")
message(STATUS "║  GPU:          ${OME_ENABLE_GPU}")
message(STATUS "║  NDI:          ${OME_ENABLE_NDI}")
message(STATUS "║  SRT:          ${OME_ENABLE_SRT}")
message(STATUS "║  WebRTC:       ${OME_ENABLE_WEBRTC}")
message(STATUS "║  ST2110:       ${OME_ENABLE_ST2110}")
message(STATUS "║  CEF:          ${OME_ENABLE_CEF}")
message(STATUS "║  Tests:        ${OME_BUILD_TESTS}")
message(STATUS "║  Samples:      ${OME_BUILD_SAMPLES}")
message(STATUS "╚══════════════════════════════════════════════════╝")
message(STATUS "")
