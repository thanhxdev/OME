# Third-Party Libraries

This directory contains vendored and bundled third-party libraries that are not available via vcpkg.

## Libraries

| Directory | Library | License | Status |
|-----------|---------|---------|--------|
| `ffmpeg/` | FFmpeg 6.1+ | LGPL 2.1+ / GPL | Pre-built binaries |
| `ndi_sdk/` | NDI SDK 6 | Proprietary | Requires NDI developer account |
| `decklink_sdk/` | DeckLink SDK 13.x | Proprietary (free) | Headers only |
| `aja_sdk/` | AJA NTV2 SDK 17.x | MIT | Open source |
| `magewell_sdk/` | Magewell Pro Capture SDK | Proprietary (free) | For Magewell hardware |
| `cef/` | Chromium Embedded Framework | BSD | For HTML overlay |
| `nvidia_codec_sdk/` | NVIDIA Video Codec SDK | Proprietary (free) | Headers only, runtime via driver |
| `intel_onevpl/` | Intel oneVPL | MIT | Open source |

## Setup Instructions

1. **FFmpeg**: Download pre-built from https://github.com/BtbN/FFmpeg-Builds/releases
   - Extract to `ffmpeg/` maintaining `include/` and `lib/` structure

2. **NDI SDK**: Register at https://ndi.video/for-developers/ndi-sdk/
   - Extract to `ndi_sdk/`

3. **DeckLink SDK**: Download from https://www.blackmagicdesign.com/developer/product/capture-and-playback
   - Extract headers to `decklink_sdk/include/`

4. **AJA NTV2**: Clone from https://github.com/aja-video/ntv2
   - Build and place in `aja_sdk/`

5. **CEF**: Download from https://cef-builds.spotifycdn.com/index.html
   - Extract to `cef/`

Or use the automated setup script:
```powershell
.\tools\scripts\setup_env.ps1 -DownloadSDKs
```

## Important Notes

- All SDKs are `.gitignore`d — they must be downloaded/installed per developer machine
- Only headers are committed for proprietary SDKs where licensing allows
- Runtime DLLs for NVIDIA codec SDK ship with the driver
