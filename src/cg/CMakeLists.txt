cmake_minimum_required(VERSION 3.28)

project(OpenMedia.CG)

file(GLOB_RECURSE SRC_FILES "src/*.cpp")
file(GLOB_RECURSE HEADERS "include/openmedia/cg/*.h")

add_library(OpenMedia.CG STATIC ${SRC_FILES} ${HEADERS})

target_include_directories(OpenMedia.CG
    PUBLIC 
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

# CEF Integration
set(CEF_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../third_party/cef")
if(EXISTS "${CEF_ROOT}/include/cef_app.h")
    message(STATUS "CEF found at ${CEF_ROOT}. Enabling HTML CG rendering.")
    target_compile_definitions(OpenMedia.CG PUBLIC OME_ENABLE_CEF)
    target_include_directories(OpenMedia.CG PRIVATE "${CEF_ROOT}")
    
    # In a real CMake setup, CEF is usually added via find_package or add_subdirectory.
    # For now, we stub the include path. The user must build libcef_dll_wrapper.
    if(MSVC)
        target_link_libraries(OpenMedia.CG PRIVATE 
            "${CEF_ROOT}/Release/libcef.lib"
            "${CEF_ROOT}/Release/libcef_dll_wrapper.lib"
        )
    endif()
else()
    message(WARNING "CEF not found in ${CEF_ROOT}. HTML CG rendering will be disabled.")
endif()

target_link_libraries(OpenMedia.CG
    PUBLIC
        OpenMedia.Core
)
