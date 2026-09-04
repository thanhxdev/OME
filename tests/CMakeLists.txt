# ============================================================
# Tests — CMakeLists.txt
# ============================================================

find_package(GTest REQUIRED)

if(GTest_FOUND)
    include(GoogleTest)

    # --- Core Unit Tests ---
    add_executable(test_core
        unit/core/test_types.cpp
        unit/core/test_error_codes.cpp
        unit/core/test_memory_pool.cpp
        unit/core/test_media_frame.cpp
        unit/core/test_frame_queue.cpp
        unit/core/test_clock_sync.cpp
        unit/core/test_pipeline_graph.cpp
        unit/core/test_tee_node.cpp
    )

    target_link_libraries(test_core
        PRIVATE
            OpenMedia.Core
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_core)

    # --- IPC / Exhand Unit Tests ---
    add_executable(test_ipc
        unit/ipc/test_command_types.cpp
        unit/ipc/test_command_dispatcher.cpp
        unit/ipc/test_worker_pool.cpp
        unit/ipc/test_shared_memory.cpp
    )

    target_link_libraries(test_ipc
        PRIVATE
            OpenMedia.Core
            OpenMedia.IPC
            OpenMedia.CommandDispatcher
            OpenMedia.WorkerPool
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_ipc)

    # --- Plugin Host Unit Tests ---
    add_executable(test_plugin_host
        unit/plugin_host/test_plugin_host.cpp
        unit/plugin_host/test_plugin_sandbox.cpp
    )

    target_link_libraries(test_plugin_host
        PRIVATE
            OpenMedia.Core
            OpenMedia.PluginHost
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_plugin_host)

    # --- Plugins Unit & Integration Tests ---
    add_executable(test_plugins
        unit/plugins/test_plugin_loader.cpp
        unit/plugins/test_plugin_manager.cpp
        integration/test_hot_reload.cpp
    )

    target_link_libraries(test_plugins
        PRIVATE
            OpenMedia.Core
            OpenMedia.PluginSDK
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_plugins)


    # --- Integration Tests ---
    add_executable(test_integration_core
        integration/test_stress_memory.cpp
    )

    target_link_libraries(test_integration_core
        PRIVATE
            OpenMedia.Core
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_integration_core)

    add_executable(test_integration_ipc
        integration/test_ipc_client_server.cpp
        integration/test_shared_memory_transfer.cpp
        integration/test_d3d11_shared_texture.cpp
    )

    target_link_libraries(test_integration_ipc
        PRIVATE
            OpenMedia.Core
            OpenMedia.IPC
            OpenMedia.CommandDispatcher
            OpenMedia.WorkerPool
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_integration_ipc)

    # --- IO Unit Tests ---
    add_executable(test_io
        unit/io/test_media_reader.cpp
        unit/io/test_file_source.cpp
        unit/io/test_live_source.cpp
        unit/io/test_device_source.cpp
        unit/io/test_file_output.cpp
    )

    target_link_libraries(test_io
        PRIVATE
            OpenMedia.Core
            OpenMedia.IO
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_io)

    # --- Codecs Unit & Integration Tests ---
    add_executable(test_codecs
        unit/codecs/test_codec_pipeline.cpp
        unit/codecs/test_aac_encoder.cpp
        unit/codecs/test_nvenc.cpp
        unit/codecs/test_quicksync.cpp
        integration/test_pipeline_transcode.cpp
        integration/test_snapshot.cpp
        integration/test_multi_format.cpp
        integration/test_e2e_pipeline.cpp
    )

    target_link_libraries(test_codecs
        PRIVATE
            OpenMedia.Core
            OpenMedia.IO
            OpenMedia.Codecs
            OpenMedia.Mixer
            OpenMedia.Rendering
            OpenMedia.Audio
            OpenMedia.Overlay
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_codecs)
    
    # --- Rendering Unit Tests ---
    add_executable(test_rendering
        unit/rendering/test_renderer.cpp
    )
    target_link_libraries(test_rendering
        PRIVATE
            OpenMedia.Core
            OpenMedia.Rendering
            GTest::gtest
            GTest::gtest_main
    )
    gtest_discover_tests(test_rendering)


    # --- Mixer Unit Tests ---
    add_executable(test_mixer
        unit/mixer/test_mixer_layer.cpp
        unit/mixer/test_mixer.cpp
        unit/mixer/test_transition.cpp
        unit/mixer/test_switcher.cpp
        unit/mixer/test_chroma_key.cpp
        unit/mixer/test_luma_key.cpp
        unit/mixer/test_scale_filter.cpp
    )

    target_link_libraries(test_mixer
        PRIVATE
            OpenMedia.Core
            OpenMedia.Mixer
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_mixer)

    # --- Audio Unit Tests ---
    add_executable(test_audio
        unit/audio/test_audio_mixer.cpp
        unit/audio/test_audio_meter.cpp
    )

    target_link_libraries(test_audio
        PRIVATE
            OpenMedia.Core
            OpenMedia.Audio
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_audio)
    
    # --- Audio Integration Tests ---
    add_executable(test_integration_audio
        integration/test_audio_mixer_verification.cpp
    )
    target_link_libraries(test_integration_audio
        PRIVATE
            OpenMedia.Core
            OpenMedia.Audio
            GTest::gtest
            GTest::gtest_main
    )
    gtest_discover_tests(test_integration_audio)


    # --- Overlay Unit Tests ---
    add_executable(test_overlay
        unit/overlay/test_text_overlay.cpp
        unit/overlay/test_logo_overlay.cpp
        unit/overlay/test_ticker_overlay.cpp
    )

    target_link_libraries(test_overlay
        PRIVATE
            OpenMedia.Core
            OpenMedia.Overlay
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_overlay)

    # --- GPU Unit & Benchmark Tests ---
    add_executable(test_gpu
        unit/gpu/test_d3d11_context.cpp
        unit/gpu/test_cuda_context.cpp
        unit/gpu/test_quicksync.cpp
        benchmark/test_gpu_transfer.cpp
    )

    target_link_libraries(test_gpu
        PRIVATE
            OpenMedia.Core
            OpenMedia.GPU
            GTest::gtest
            GTest::gtest_main
    )

    if(WIN32)
        target_link_libraries(test_gpu PRIVATE d3d11.lib dxgi.lib)
    endif()

    gtest_discover_tests(test_gpu)

    # --- SRT Unit Tests ---
    add_executable(test_srt
        test_srt_engine.cpp
    )

    target_link_libraries(test_srt
        PRIVATE
            OpenMedia.Core
            OpenMedia_SRT
            GTest::gtest
            GTest::gtest_main
    )

    target_include_directories(test_srt PRIVATE ../src/protocols/srt/include)

    if(WIN32)
        target_link_libraries(test_srt PRIVATE ws2_32.lib)
    endif()

    gtest_discover_tests(test_srt)


    # --- NDI Unit Tests ---
    add_executable(test_ndi
        test_ndi_engine.cpp
    )

    target_link_libraries(test_ndi
        PRIVATE
            OpenMedia.Core
            OpenMedia_NDI
            GTest::gtest
            GTest::gtest_main
    )

    target_include_directories(test_ndi PRIVATE ../src/protocols/ndi/include)

    if(WIN32)
        target_link_libraries(test_ndi PRIVATE ws2_32.lib)
    endif()

    gtest_discover_tests(test_ndi)
    
    # --- Protocols Integration Tests ---
    add_executable(test_integration_protocols
        integration/test_multi_output.cpp
    )

    target_link_libraries(test_integration_protocols
        PRIVATE
            OpenMedia.Core
            OpenMedia.IO
            OpenMedia.Mixer
            OpenMedia_SRT
            OpenMedia.RTMP
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_integration_protocols)
    
    target_include_directories(test_integration_protocols PRIVATE ../src/protocols/srt/include ../src/protocols/rtmp/include)
        # --- SDK Unit Tests ---
    add_executable(test_sdk
        unit/sdk/test_sdk.cpp
        ../src/server/src/ServerApp.cpp
        ../src/server/src/PipelineSession.cpp
        ../src/server/src/PipelineEngineManager.cpp
    )

    target_include_directories(test_sdk PRIVATE ../src/server/include)

    target_link_libraries(test_sdk
        PRIVATE
            OpenMedia.Core
            OpenMedia.IO
            OpenMedia.IPC
            OpenMedia.CommandDispatcher
            OpenMedia.WorkerPool
            OpenMedia.SDK
            OpenMedia.Audio
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_sdk)

    # --- Benchmark Tests ---
    add_executable(test_benchmark
        benchmark/test_pipeline_performance.cpp
    )

    target_link_libraries(test_benchmark
        PRIVATE
            OpenMedia.Core
            OpenMedia.IO
            OpenMedia.Codecs
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_benchmark)

    # --- CG Pipeline Demo ---
    add_executable(test_cg_pipeline
        integration/test_cg_pipeline.cpp
    )

    target_link_libraries(test_cg_pipeline
        PRIVATE
            OpenMedia.Core
            OpenMedia.CG
            OpenMedia.Mixer
    )

    # Copy CEF binaries if enabled
    set(CEF_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../third_party/cef")
    if(EXISTS "${CEF_ROOT}/include/cef_app.h")
        add_custom_command(TARGET test_cg_pipeline POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_directory_if_different
            "${CEF_ROOT}/Release"
            "$<TARGET_FILE_DIR:test_cg_pipeline>"
        )
        add_custom_command(TARGET test_cg_pipeline POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_directory_if_different
            "${CEF_ROOT}/Resources"
            "$<TARGET_FILE_DIR:test_cg_pipeline>"
        )
    endif()
else()
    message(STATUS "GTest not found — skipping tests")
endif()
