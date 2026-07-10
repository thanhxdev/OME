# ============================================================
# Windows-MSVC Toolchain
# ============================================================
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_CXX_COMPILER cl)
set(CMAKE_C_COMPILER cl)

# Prefer static runtime for production
# set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
