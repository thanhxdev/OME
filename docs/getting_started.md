# Getting Started with OpenMedia SDK

Welcome to the OpenMedia SDK! This guide will walk you through the process of setting up your environment, building the SDK, and writing your first C++ media pipeline.

## Prerequisites

Before you begin, ensure you have the following installed on your system:
- **CMake** (3.28 or higher)
- **C++ Compiler** with C++23 support (MSVC 2026, GCC 13+, or Clang 16+)
- **FFmpeg** (v6.0 or higher)
- **CUDA Toolkit & NVCODEC** (Optional, for hardware acceleration)
- **NDI SDK** (Optional, for NDI protocol support)

## Building the SDK

1. Clone the repository:
   ```bash
   git clone https://github.com/your-org/openmedia-sdk.git
   cd openmedia-sdk
   ```

2. Create a build directory and configure the project:
   ```bash
   mkdir build && cd build
   cmake .. -DCMAKE_BUILD_TYPE=Release
   ```

3. Build the SDK:
   ```bash
   cmake --build . --config Release --parallel
   ```

## Your First Application: Simple Player

Here is a minimal example demonstrating how to play a local video file using OpenMedia SDK.

### `main.cpp`

```cpp
#include <openmedia/io/FileSource.h>
#include <openmedia/codecs/H264Decoder.h>
#include <openmedia/rendering/D3D11Renderer.h>
#include <iostream>
#include <thread>
#include <chrono>

using namespace openmedia::core;
using namespace openmedia::io;
using namespace openmedia::codecs;
using namespace openmedia::rendering;

int main() {
    // 1. Create pipeline components
    auto fileSource = std::make_shared<FileSource>("test_video.mp4");
    auto decoder = std::make_shared<FFmpegH264Decoder>();
    auto renderer = std::make_shared<D3D11Renderer>();

    // 2. Initialize components
    fileSource->Initialize();
    decoder->Initialize();
    renderer->Initialize();

    // 3. Connect the pipeline: Source -> Decoder -> Renderer
    fileSource->Connect(decoder);
    decoder->Connect(renderer);

    // 4. Start the pipeline (from Sink to Source)
    renderer->Start();
    decoder->Start();
    fileSource->Start();

    std::cout << "Pipeline is running..." << std::endl;

    // 5. Keep the application running
    while (renderer->GetState() == PipelineState::Running) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    // 6. Cleanup
    fileSource->Stop();
    decoder->Stop();
    renderer->Stop();

    return 0;
}
```

### `CMakeLists.txt`

```cmake
cmake_minimum_required(VERSION 3.28)
project(MyFirstApp LANGUAGES CXX)

add_executable(MyFirstApp main.cpp)

# Link against OpenMedia SDK components
target_link_libraries(MyFirstApp PRIVATE
    OpenMedia.Core
    OpenMedia.IO
    OpenMedia.Codecs
    OpenMedia.Rendering
)

set_target_properties(MyFirstApp PROPERTIES CXX_STANDARD 23)
```

## Next Steps

- Explore the **[API Reference](api_reference.md)** for a deep dive into the available interfaces.
- Read the **[Plugin Development Guide](plugin_development_guide.md)** to learn how to extend the SDK.
- If you are migrating from Medialooks, check out the **[Migration Guide](migration_guide.md)**.
