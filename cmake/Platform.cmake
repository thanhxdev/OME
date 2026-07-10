# ============================================================
# Platform.cmake
# Platform-specific settings
# ============================================================

if(WIN32)
    add_compile_definitions(
        WIN32_LEAN_AND_MEAN
        NOMINMAX
        _CRT_SECURE_NO_WARNINGS
        UNICODE
        _UNICODE
    )
    # Windows-specific libraries
    set(OME_PLATFORM_LIBS
        ws2_32
        mswsock
        advapi32
        ole32
        oleaut32
        uuid
        d3d11
        dxgi
        d3dcompiler
    )
elseif(UNIX AND NOT APPLE)
    # Linux specifics
    find_package(Threads REQUIRED)
    find_package(PkgConfig REQUIRED)
    set(OME_PLATFORM_LIBS
        Threads::Threads
        dl
        rt
    )
endif()
