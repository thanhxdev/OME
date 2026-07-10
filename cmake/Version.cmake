# ============================================================
# Version.cmake
# Git tag version extraction
# ============================================================

find_package(Git QUIET)
if(GIT_FOUND)
    execute_process(
        COMMAND ${GIT_EXECUTABLE} describe --tags --always --dirty
        WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
        OUTPUT_VARIABLE GIT_VERSION
        OUTPUT_STRIP_TRAILING_WHITESPACE
        ERROR_QUIET
    )
    execute_process(
        COMMAND ${GIT_EXECUTABLE} rev-parse --short HEAD
        WORKING_DIRECTORY ${CMAKE_SOURCE_DIR}
        OUTPUT_VARIABLE GIT_COMMIT_HASH
        OUTPUT_STRIP_TRAILING_WHITESPACE
        ERROR_QUIET
    )
    if(GIT_VERSION)
        message(STATUS "Git version: ${GIT_VERSION}")
    endif()
    if(GIT_COMMIT_HASH)
        message(STATUS "Git commit:  ${GIT_COMMIT_HASH}")
    endif()
else()
    set(GIT_VERSION "unknown")
    set(GIT_COMMIT_HASH "unknown")
    message(STATUS "Git not found — version set to unknown")
endif()
