cmake_minimum_required(VERSION 3.28)

# OpenMediaServer.exe
add_executable(OpenMediaServer
    src/main.cpp
    src/ServerApp.cpp
)

target_include_directories(OpenMediaServer
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
)

target_link_libraries(OpenMediaServer
    PRIVATE
        OpenMedia.Core
        OpenMedia.IPC
        OpenMedia.CommandDispatcher
        OpenMedia.WorkerPool
        OpenMedia.IO
        OpenMedia.Audio
)

if(WIN32)
    target_link_libraries(OpenMediaServer PRIVATE Kernel32)
endif()

target_compile_features(OpenMediaServer PUBLIC cxx_std_23)

# Install
install(TARGETS OpenMediaServer
    RUNTIME DESTINATION bin
)
