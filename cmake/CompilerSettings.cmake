# ============================================================
# CompilerSettings.cmake
# Compiler flags per-platform
# ============================================================

if(MSVC)
    add_compile_options(
        /W4           # Warning level 4
        # /WX         # Warnings as errors (enable later)
        /permissive-  # Standards conformance
        /MP           # Multi-processor compilation
        /Zc:__cplusplus  # Correct __cplusplus macro
        /utf-8        # Source and execution charset
        /EHsc         # Exception handling model
    )
    # Release optimizations
    if(CMAKE_BUILD_TYPE STREQUAL "Release")
        add_compile_options(/O2 /GL /Oi /Gy)
        add_link_options(/LTCG /OPT:REF /OPT:ICF)
    endif()
elseif(CMAKE_CXX_COMPILER_ID MATCHES "Clang|GNU")
    add_compile_options(
        -Wall -Wextra -Wpedantic
        -Wno-unused-parameter
        -fPIC
    )
    if(CMAKE_BUILD_TYPE STREQUAL "Release")
        add_compile_options(-O2 -march=native)
    endif()
endif()
