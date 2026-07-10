# ============================================================
# Tests — CMakeLists.txt
# ============================================================

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
    )

    target_link_libraries(test_core
        PRIVATE
            OpenMedia.Core
            GTest::gtest
            GTest::gtest_main
    )

    gtest_discover_tests(test_core)
else()
    message(STATUS "GTest not found — skipping unit tests")
endif()
