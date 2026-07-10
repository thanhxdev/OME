# OpenMedia SDK — Testing Plan

**Version:** 1.0  
**Date:** July 2026

---

## 1. Testing Strategy

| Level | Framework | Scope | Run Frequency |
|-------|-----------|-------|---------------|
| **Unit Tests** | Google Test (gtest) | Individual classes, functions | Every commit |
| **Integration Tests** | Google Test + custom | Module interactions, pipelines | Every PR |
| **Performance Benchmarks** | Google Benchmark | Throughput, latency, memory | Weekly / Release |
| **Fuzz Testing** | libFuzzer / AFL | Parsers, demuxers, protocol handlers | Nightly |
| **.NET Tests** | xUnit / NUnit | Managed wrapper correctness | Every commit |
| **End-to-End Tests** | Custom framework | Full workflow scenarios | Pre-release |

---

## 2. Unit Tests

### 2.1 Test Structure

```
tests/
├── CMakeLists.txt
├── unit/
│   ├── core/
│   │   ├── test_media_frame.cpp
│   │   ├── test_frame_queue.cpp
│   │   ├── test_media_pipeline.cpp
│   │   ├── test_clock_sync.cpp
│   │   ├── test_memory_pool.cpp
│   │   ├── test_config.cpp
│   │   └── test_engine.cpp
│   ├── io/
│   │   ├── test_file_source.cpp
│   │   ├── test_media_reader.cpp
│   │   ├── test_live_source.cpp
│   │   └── test_device_factory.cpp
│   ├── codecs/
│   │   ├── test_h264_encoder.cpp
│   │   ├── test_h264_decoder.cpp
│   │   ├── test_h265_encoder.cpp
│   │   ├── test_aac_encoder.cpp
│   │   └── test_codec_factory.cpp
│   ├── mixer/
│   │   ├── test_mixer.cpp
│   │   ├── test_mixer_layer.cpp
│   │   ├── test_transition.cpp
│   │   └── test_chroma_key.cpp
│   ├── audio/
│   │   ├── test_audio_mixer.cpp
│   │   ├── test_audio_meter.cpp
│   │   ├── test_resampler.cpp
│   │   └── test_channel_mapper.cpp
│   ├── overlay/
│   │   ├── test_text_overlay.cpp
│   │   ├── test_logo_overlay.cpp
│   │   └── test_ticker_overlay.cpp
│   ├── gpu/
│   │   ├── test_gpu_context.cpp
│   │   ├── test_gpu_frame.cpp
│   │   └── test_gpu_transfer.cpp
│   └── protocols/
│       ├── test_srt_engine.cpp
│       ├── test_ndi_engine.cpp
│       ├── test_rtmp_engine.cpp
│       └── test_webrtc_engine.cpp
```

### 2.2 Unit Test Examples

```cpp
// tests/unit/core/test_frame_queue.cpp
#include <gtest/gtest.h>
#include <openmedia/core/FrameQueue.h>
#include <thread>

using namespace openmedia::core;

class FrameQueueTest : public ::testing::Test {
protected:
    void SetUp() override {
        queue = std::make_unique<FrameQueue>(8); // capacity = 8
    }
    std::unique_ptr<FrameQueue> queue;
};

TEST_F(FrameQueueTest, InitiallyEmpty) {
    EXPECT_TRUE(queue->IsEmpty());
    EXPECT_EQ(queue->Size(), 0);
}

TEST_F(FrameQueueTest, PushAndPop) {
    auto frame = MediaFrame::Allocate({1920, 1080, PixelFormat::NV12});
    ASSERT_TRUE(queue->Push(std::move(frame)));
    EXPECT_EQ(queue->Size(), 1);

    auto popped = queue->Pop();
    ASSERT_NE(popped, nullptr);
    EXPECT_TRUE(queue->IsEmpty());
}

TEST_F(FrameQueueTest, ThreadSafety) {
    const int NUM_FRAMES = 1000;
    std::atomic<int> produced{0}, consumed{0};

    // Producer thread
    std::thread producer([&]() {
        for (int i = 0; i < NUM_FRAMES; ++i) {
            auto frame = MediaFrame::Allocate({1920, 1080, PixelFormat::NV12});
            while (!queue->Push(std::move(frame))) {
                std::this_thread::yield();
            }
            produced++;
        }
    });

    // Consumer thread
    std::thread consumer([&]() {
        while (consumed < NUM_FRAMES) {
            auto frame = queue->Pop(std::chrono::milliseconds(10));
            if (frame) consumed++;
        }
    });

    producer.join();
    consumer.join();

    EXPECT_EQ(produced.load(), NUM_FRAMES);
    EXPECT_EQ(consumed.load(), NUM_FRAMES);
}

TEST_F(FrameQueueTest, BackpressureWhenFull) {
    // Fill queue to capacity
    for (int i = 0; i < 8; ++i) {
        auto frame = MediaFrame::Allocate({1920, 1080, PixelFormat::NV12});
        ASSERT_TRUE(queue->Push(std::move(frame)));
    }
    EXPECT_TRUE(queue->IsFull());

    // Should fail (non-blocking)
    auto extra = MediaFrame::Allocate({1920, 1080, PixelFormat::NV12});
    EXPECT_FALSE(queue->TryPush(std::move(extra)));
}
```

### 2.3 Test CMakeLists.txt

```cmake
# tests/CMakeLists.txt
find_package(GTest REQUIRED)
find_package(benchmark QUIET)

# Helper function
function(ome_add_test TEST_NAME SOURCE)
    add_executable(${TEST_NAME} ${SOURCE})
    target_link_libraries(${TEST_NAME}
        PRIVATE
            GTest::gtest_main
            ${ARGN}  # Additional link libraries
    )
    add_test(NAME ${TEST_NAME} COMMAND ${TEST_NAME})
    set_tests_properties(${TEST_NAME} PROPERTIES TIMEOUT 30)
endfunction()

# Core tests
ome_add_test(test_frame_queue unit/core/test_frame_queue.cpp OpenMedia.Core)
ome_add_test(test_media_frame unit/core/test_media_frame.cpp OpenMedia.Core)
ome_add_test(test_pipeline unit/core/test_media_pipeline.cpp OpenMedia.Core)
ome_add_test(test_clock_sync unit/core/test_clock_sync.cpp OpenMedia.Core)
ome_add_test(test_config unit/core/test_config.cpp OpenMedia.Core)

# IO tests
ome_add_test(test_file_source unit/io/test_file_source.cpp OpenMedia.IO OpenMedia.Core)
ome_add_test(test_media_reader unit/io/test_media_reader.cpp OpenMedia.IO OpenMedia.Core)

# Codec tests
ome_add_test(test_h264_encoder unit/codecs/test_h264_encoder.cpp OpenMedia.Codecs OpenMedia.Core)
ome_add_test(test_h264_decoder unit/codecs/test_h264_decoder.cpp OpenMedia.Codecs OpenMedia.Core)

# Mixer tests
ome_add_test(test_mixer unit/mixer/test_mixer.cpp OpenMedia.Mixer OpenMedia.Core)

# Audio tests
ome_add_test(test_audio_mixer unit/audio/test_audio_mixer.cpp OpenMedia.Audio OpenMedia.Core)
ome_add_test(test_audio_meter unit/audio/test_audio_meter.cpp OpenMedia.Audio OpenMedia.Core)
```

---

## 3. Integration Tests

### 3.1 Pipeline Integration Tests

```cpp
// tests/integration/pipeline_tests/test_file_to_file.cpp
#include <gtest/gtest.h>
#include <openmedia/Engine.h>

TEST(PipelineIntegration, FileToFile_H264) {
    auto engine = openmedia::Engine::Create();

    auto source = engine->CreateFileSource("test_data/sample_1080p.mp4");
    auto encoder = engine->CreateEncoder({
        .codec = "h264", .bitrate = 5000, .preset = "ultrafast"
    });
    auto output = engine->CreateFileOutput("output_test.mp4", encoder->GetConfig());

    auto pipeline = engine->CreatePipeline();
    pipeline->SetSource(source.get())
            .SetEncoder(encoder.get())
            .AddOutput(output.get())
            .Build();

    pipeline->Start();

    // Wait for completion or timeout
    auto state = pipeline->WaitForCompletion(std::chrono::seconds(60));
    EXPECT_EQ(state, PipelineState::Completed);

    // Verify output file exists and is valid
    EXPECT_TRUE(std::filesystem::exists("output_test.mp4"));
    EXPECT_GT(std::filesystem::file_size("output_test.mp4"), 0);

    // Cleanup
    std::filesystem::remove("output_test.mp4");
}

TEST(PipelineIntegration, MixerWithTwoSources) {
    auto engine = openmedia::Engine::Create();

    auto source1 = engine->CreateFileSource("test_data/video1.mp4");
    auto source2 = engine->CreateFileSource("test_data/video2.mp4");

    auto mixer = engine->CreateMixer({.width = 1920, .height = 1080});
    mixer->AddInput(source1.get(), 0);
    mixer->AddInput(source2.get(), 1);

    auto encoder = engine->CreateEncoder({.codec = "h264", .bitrate = 5000});
    auto output = engine->CreateFileOutput("mixer_test.mp4", encoder->GetConfig());

    auto pipeline = engine->CreatePipeline();
    pipeline->SetSource(mixer.get())
            .SetEncoder(encoder.get())
            .AddOutput(output.get())
            .Build();

    pipeline->Start();

    // Process for 5 seconds
    std::this_thread::sleep_for(std::chrono::seconds(5));
    pipeline->Stop();

    EXPECT_TRUE(std::filesystem::exists("mixer_test.mp4"));
    std::filesystem::remove("mixer_test.mp4");
}
```

---

## 4. Performance Benchmarks

### 4.1 Encoding Benchmark

```cpp
// tests/benchmark/encoding_bench/bench_h264_encode.cpp
#include <benchmark/benchmark.h>
#include <openmedia/Engine.h>

static void BM_H264Encode_1080p(benchmark::State& state) {
    auto engine = openmedia::Engine::Create();
    auto encoder = engine->CreateEncoder({
        .codec = "h264",
        .width = 1920, .height = 1080,
        .bitrate = 8000,
        .preset = state.range(0) == 0 ? "ultrafast" : "medium"
    });

    auto frame = MediaFrame::Allocate({1920, 1080, PixelFormat::NV12});

    for (auto _ : state) {
        std::vector<uint8_t> encoded;
        encoder->EncodeFrame(*frame, encoded);
    }

    state.SetItemsProcessed(state.iterations());
    state.SetLabel(state.range(0) == 0 ? "ultrafast" : "medium");
}

BENCHMARK(BM_H264Encode_1080p)->Arg(0)->Arg(1)->Unit(benchmark::kMillisecond);

static void BM_H264Encode_4K(benchmark::State& state) {
    auto engine = openmedia::Engine::Create();
    auto encoder = engine->CreateEncoder({
        .codec = "h264",
        .width = 3840, .height = 2160,
        .bitrate = 20000,
        .preset = "fast"
    });

    auto frame = MediaFrame::Allocate({3840, 2160, PixelFormat::NV12});

    for (auto _ : state) {
        std::vector<uint8_t> encoded;
        encoder->EncodeFrame(*frame, encoded);
    }

    state.SetItemsProcessed(state.iterations());
}

BENCHMARK(BM_H264Encode_4K)->Unit(benchmark::kMillisecond);

BENCHMARK_MAIN();
```

### 4.2 Target Metrics

| Metric | Target | Notes |
|--------|--------|-------|
| H.264 1080p encode (CPU) | ≥ 60 fps | ultrafast preset |
| H.264 1080p encode (GPU) | ≥ 240 fps | NVENC |
| H.265 1080p encode (GPU) | ≥ 120 fps | NVENC |
| Decode 1080p | ≥ 120 fps | CPU/GPU |
| Frame copy (CPU→GPU) | < 1 ms | 1080p NV12 |
| Pipeline latency | < 50 ms | Source → Output |
| Memory per 1080p pipeline | < 200 MB | Steady state |
| SRT round-trip latency | < 200 ms | Internet |
| NDI latency | < 1 frame | LAN |

---

## 5. Memory & Sanitizer Testing

### 5.1 AddressSanitizer (Demo build)

```powershell
# Build with sanitizers (auto-enabled in demo env)
cmake -B build-asan -DOME_ENV_TAG=demo -DCMAKE_BUILD_TYPE=Debug
cmake --build build-asan

# Run tests with ASan
ctest --test-dir build-asan --output-on-failure
```

### 5.2 Memory Leak Detection

```cpp
// Custom test fixture with memory tracking
class MemoryTrackingTest : public ::testing::Test {
protected:
    void SetUp() override {
        initialMemory = GetCurrentMemoryUsage();
    }

    void TearDown() override {
        auto finalMemory = GetCurrentMemoryUsage();
        auto leaked = finalMemory - initialMemory;
        EXPECT_LT(leaked, 1024) << "Memory leak detected: " << leaked << " bytes";
    }

private:
    size_t initialMemory;
};
```

---

## 6. Test Data

### Test media files (stored in `tests/test_data/`)

| File | Format | Resolution | Duration | Purpose |
|------|--------|-----------|----------|---------|
| `sample_1080p.mp4` | H.264/AAC | 1920x1080 | 10s | Basic decode/encode test |
| `sample_4k.mp4` | H.265/AAC | 3840x2160 | 5s | 4K pipeline test |
| `sample_audio.wav` | PCM | 48kHz/16bit/stereo | 5s | Audio processing test |
| `sample_hdr.mp4` | H.265 HDR10 | 1920x1080 | 5s | HDR pipeline test |
| `test_pattern.png` | PNG | 1920x1080 | - | Overlay/mixer test |
| `logo.png` | PNG+Alpha | 200x200 | - | Logo overlay test |
| `interlaced.mxf` | MPEG-2 | 1920x1080i | 5s | Interlace handling |

> **Lưu ý:** Test data files không được commit vào git. Sử dụng `tools/scripts/download_test_data.ps1` để tải về.

---

## 7. CI Test Pipeline

```yaml
# Chạy trong CI
test-matrix:
  - name: "Unit Tests (Demo)"
    env: demo
    command: ctest --test-dir build-demo -C Debug --output-on-failure

  - name: "Unit Tests (Production)"
    env: production
    command: ctest --test-dir build-prod -C Release --output-on-failure

  - name: "Integration Tests"
    env: demo
    command: ctest --test-dir build-demo -C Debug -L integration --output-on-failure

  - name: "Benchmarks"
    env: production
    command: ./build-prod/bin/benchmark_suite --benchmark_format=json --benchmark_out=results.json

  - name: ".NET Tests"
    command: dotnet test wrappers/OpenMedia.NET.sln --logger "trx;LogFileName=results.trx"
```

---

## 8. Code Coverage

```powershell
# Generate coverage report (demo build with llvm-cov)
cmake -B build-coverage -DOME_ENV_TAG=demo -DCMAKE_BUILD_TYPE=Debug -DOME_ENABLE_COVERAGE=ON
cmake --build build-coverage
ctest --test-dir build-coverage
llvm-cov report ./build-coverage/bin/test_* --instr-profile=default.profdata

# Target: ≥ 80% line coverage for core modules
```
