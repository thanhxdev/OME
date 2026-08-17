cmake_minimum_required(VERSION 3.20)

project(OpenMedia_NDI)

set(SOURCES
    src/NDIEngine.cpp
    src/NDISource.cpp
    src/NDIOutput.cpp
    ${CMAKE_SOURCE_DIR}/third_party/ndi_sdk/src/Processing.NDI.Lib.cpp
)

set(HEADERS
    include/openmedia/ndi/NDIEngine.h
    include/openmedia/ndi/NDISource.h
    include/openmedia/ndi/NDIOutput.h
)

add_library(OpenMedia_NDI STATIC ${SOURCES} ${HEADERS})

target_include_directories(OpenMedia_NDI
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_SOURCE_DIR}/third_party/ndi_sdk/include
)

target_link_libraries(OpenMedia_NDI
    PUBLIC
        OpenMedia.Core
)

# Using Mock NDI SDK (sources added directly above)
