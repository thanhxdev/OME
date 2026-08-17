# ============================================================
# EnvironmentConfig.cmake
# Load environment variables based on OME_ENV_TAG
# ============================================================

# Determine environment tag
if(NOT DEFINED OME_ENV_TAG)
    set(OME_ENV_TAG "demo" CACHE STRING "Environment tag: demo or production")
endif()

message(STATUS "╔══════════════════════════════════════════╗")
message(STATUS "║  OpenMedia SDK Environment: ${OME_ENV_TAG}")
message(STATUS "╚══════════════════════════════════════════╝")

# Load shared environment
set(ENV_SHARED_FILE "${CMAKE_SOURCE_DIR}/.env.shared")
set(ENV_TAG_FILE "${CMAKE_SOURCE_DIR}/.env.${OME_ENV_TAG}")

# Parse .env file helper function
function(load_env_file FILE_PATH)
    if(EXISTS "${FILE_PATH}")
        message(STATUS "Loading env file: ${FILE_PATH}")
        file(STRINGS "${FILE_PATH}" ENV_LINES)
        foreach(LINE IN LISTS ENV_LINES)
            # Skip comments and empty lines
            string(REGEX MATCH "^[#]" IS_COMMENT "${LINE}")
            string(STRIP LINE_STRIPPED "${LINE}")
            if(NOT IS_COMMENT AND NOT "${LINE_STRIPPED}" STREQUAL "")
                string(REGEX MATCH "^([^=]+)=(.*)$" _ "${LINE_STRIPPED}")
                if(CMAKE_MATCH_1 AND CMAKE_MATCH_2)
                    string(STRIP KEY "${CMAKE_MATCH_1}")
                    string(STRIP VALUE "${CMAKE_MATCH_2}")
                    set("${KEY}" "${VALUE}" PARENT_SCOPE)
                    message(VERBOSE "  ${KEY} = ${VALUE}")
                endif()
            endif()
        endforeach()
    else()
        message(WARNING "Environment file not found: ${FILE_PATH}")
    endif()
endfunction()

# Load shared first, then tag-specific (overrides shared)
load_env_file("${ENV_SHARED_FILE}")
load_env_file("${ENV_TAG_FILE}")

# Apply build type
if(OME_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE "${OME_BUILD_TYPE}" CACHE STRING "" FORCE)
endif()

# Apply compiler settings based on environment
if(OME_ENV_TAG STREQUAL "production")
    add_compile_definitions(OME_PRODUCTION=1)
    add_compile_definitions(NDEBUG)
    message(STATUS "OME_LTO IS: '${OME_LTO}'")
    if(OME_LTO STREQUAL "true")
        set(CMAKE_INTERPROCEDURAL_OPTIMIZATION TRUE)
    endif()
elseif(OME_ENV_TAG STREQUAL "demo")
    add_compile_definitions(OME_DEMO=1)
    add_compile_definitions(OME_DEBUG=1)
endif()

# Generate config header
configure_file(
    "${CMAKE_SOURCE_DIR}/cmake/OpenMediaConfig.h.in"
    "${CMAKE_BINARY_DIR}/generated/OpenMediaConfig.h"
    @ONLY
)

# Make generated header available
include_directories("${CMAKE_BINARY_DIR}/generated")
