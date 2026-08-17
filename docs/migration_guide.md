# Migration Guide: Medialooks to OpenMedia SDK

If you are migrating an existing application built on top of Medialooks MPlatform or MFormats to the OpenMedia SDK, this guide provides a conceptual mapping of APIs, objects, and paradigms.

## Core Architectural Differences

- **COM vs Standard C++**: Medialooks relies heavily on Microsoft COM (`IUnknown`, `CoCreateInstance`, HRESULTs). OpenMedia SDK uses modern standard C++23 (`std::shared_ptr`, `std::expected`, modern standard library).
- **Pull vs Push**: MFormats uses a Pull model (you explicitly call `SourceFrameGet` and `SinkFramePut`). OpenMedia SDK uses a **Push Pipeline** where objects connect via `Connect()` and frames flow asynchronously via `PushFrame()` using lock-free queues.
- **Interfaces**: Instead of monolithic interfaces like `IMFormat` or `IMFrame`, OpenMedia splits responsibilities into focused interfaces.

## Object Mapping

| Medialooks MFormats / MPlatform | OpenMedia SDK Equivalent | Description |
|----------------------------------|--------------------------|-------------|
| `MFReader` / `MFile`             | `io::FileSource`         | Reads media from local files. |
| `MFWriter` / `MWriter`           | `io::FileSink`           | Writes encoded media to files. |
| `IMFrame`                        | `core::MediaFrame`       | Represents an audio/video/subtitle frame. |
| `MFDevice_I` / `MLive`           | `io::DeckLinkSource` / `io::AJASource` | Captures from hardware devices (Blackmagic, AJA). |
| `MFDevice_O` / `MRenderer`       | `io::DeckLinkSink`       | Plays out to hardware devices. |
| `MMixer`                         | `mixer::Mixer`           | Mixes multiple video and audio streams. |
| `CG` (Character Generator)       | `cg::CGEngine`           | HTML/Web-based overlay engine (CEF). |
| `MItem`                          | `mixer::MixerLayer`      | A layer inside the Mixer. |
| `MFConvert`                      | `codecs::*Decoder` / `*Encoder` | Decoding and Encoding are split into distinct classes. |

## Feature Mapping

### 1. Frame Retrieval (MFormats)

**Medialooks (Pull):**
```cpp
IMFrame* pFrame = nullptr;
pReader->SourceFrameGet(-1, &pFrame, "");
pWriter->SinkFramePut(pFrame);
```

**OpenMedia SDK (Push):**
```cpp
// You connect components, and the flow happens automatically on Start()
fileSource->Connect(h264Encoder);
h264Encoder->Connect(fileSink);

fileSink->Start();
h264Encoder->Start();
fileSource->Start();
```

### 2. Audio Processing

In Medialooks, audio levels and channel manipulation are often done directly on the `IMFrame`. In OpenMedia SDK, audio undergoes dedicated processing through the `audio::AudioMixer` and `audio::AudioMeter`.

### 3. Graphics & Overlays (CG)

Medialooks CG relies on proprietary tags or Flash/HTML combinations.
OpenMedia SDK's `CGEngine` is built on Chromium Embedded Framework (CEF), allowing you to build overlays using modern Web Technologies (React, Vue, WebGL, CSS Animations) and pass dynamic JSON data to the overlays.

## Memory Management

Forget about `AddRef()` and `Release()`. OpenMedia uses `std::shared_ptr`. When the last reference to a `MediaFrame` or `IMediaObject` goes out of scope, memory is safely and automatically cleaned up.
