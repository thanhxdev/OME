add_executable(d3d11_ipc_poc main.cpp)

target_link_libraries(d3d11_ipc_poc
    PRIVATE
        OpenMedia.Core
        OpenMedia.IPC
)

if(WIN32)
    target_link_libraries(d3d11_ipc_poc PRIVATE d3d11.lib dxgi.lib)
endif()
