# OpenMedia SDK API Reference

This document describes how to generate and access the C++ API Reference for the OpenMedia SDK using Doxygen.

## Generating the Documentation

Ensure you have [Doxygen](https://www.doxygen.nl/download.html) installed on your system.

From the root directory of the OpenMedia SDK repository, run the following command:

```bash
doxygen Doxyfile
```

This will parse the `include` directories of all core and sub-modules and generate the documentation in the `docs/doxygen/html` directory.

## Viewing the Documentation

Once generated, open the `index.html` file in your preferred web browser:

```bash
start docs/doxygen/html/index.html
```

## Key Modules to Explore

- **`openmedia::core`**: Base interfaces (`IMediaObject`, `MediaFrame`, `Engine`).
- **`openmedia::io`**: Input and Output sources (`FileSource`, `DeckLinkSource`).
- **`openmedia::codecs`**: Encoders and Decoders (`FFmpegH264Decoder`, `NVENCEncoder`).
- **`openmedia::audio`**: Audio processing (`AudioMixer`, `AudioMeter`).
- **`openmedia::rendering`**: Output renderers (`D3D11Renderer`).
- **`openmedia::overlay`**: Graphics overlay engine.

## Quick Core API Summary

While Doxygen provides the full reference, here are the most critical interfaces to understand:

### `openmedia::core::IMediaObject`
The fundamental building block for all pipeline nodes.
- `virtual bool Connect(IMediaObject* downstream) = 0;`
- `virtual bool Start() = 0;`
- `virtual bool Stop() = 0;`
- `virtual bool PushFrame(MediaFrame* frame) = 0;`

### `openmedia::core::MediaPipeline`
The orchestrator that links media objects together into a directed acyclic graph.
- `MediaPipeline& SetSource(IMediaObject* source);`
- `MediaPipeline& AddFilter(IMediaObject* filter);`
- `MediaPipeline& AddOutput(IMediaObject* output);`
- `bool Build();`
- `bool Start();`

### `openmedia::core::MediaFrame`
The universal container passing video/audio payload across the pipeline.
- `uint8_t* GetVideoData(int plane = 0);`
- `VideoFrameInfo GetVideoInfo() const;`
- `bool IsGPUFrame() const;`
