using System;
using Microsoft.Win32.SafeHandles;

namespace OpenMedia.SDK.SafeHandles
{
    /// <summary>
    /// Type-safe SafeHandle for native Pipeline instances.
    /// </summary>
    public sealed class SafePipelineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePipelineHandle() : base(ownsHandle: true) { }

        public SafePipelineHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_pipeline_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafePipelineHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native Media Source instances.
    /// </summary>
    public sealed class SafeMediaSourceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeMediaSourceHandle() : base(ownsHandle: true) { }

        public SafeMediaSourceHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_source_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeMediaSourceHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native Video Mixer instances.
    /// </summary>
    public sealed class SafeVideoMixerHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeVideoMixerHandle() : base(ownsHandle: true) { }

        public SafeVideoMixerHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_mixer_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeVideoMixerHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native Audio Mixer instances.
    /// </summary>
    public sealed class SafeAudioMixerHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeAudioMixerHandle() : base(ownsHandle: true) { }

        public SafeAudioMixerHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_audio_mixer_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeAudioMixerHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native Audio Meter instances.
    /// </summary>
    public sealed class SafeAudioMeterHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeAudioMeterHandle() : base(ownsHandle: true) { }

        public SafeAudioMeterHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_audio_meter_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeAudioMeterHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native Clock Overlay instances.
    /// </summary>
    public sealed class SafeOverlayHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeOverlayHandle() : base(ownsHandle: true) { }

        public SafeOverlayHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_overlay_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeOverlayHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native SRT Engine instances.
    /// </summary>
    public sealed class SafeSrtEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeSrtEngineHandle() : base(ownsHandle: true) { }

        public SafeSrtEngineHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_srt_engine_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeSrtEngineHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native SRT Source instances.
    /// </summary>
    public sealed class SafeSrtSourceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeSrtSourceHandle() : base(ownsHandle: true) { }

        public SafeSrtSourceHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_srt_source_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeSrtSourceHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native SRT Output instances.
    /// </summary>
    public sealed class SafeSrtOutputHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeSrtOutputHandle() : base(ownsHandle: true) { }

        public SafeSrtOutputHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_srt_output_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeSrtOutputHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native NDI Engine instances.
    /// </summary>
    public sealed class SafeNdiEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeNdiEngineHandle() : base(ownsHandle: true) { }

        public SafeNdiEngineHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_ndi_engine_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeNdiEngineHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native WebRTC Engine instances.
    /// </summary>
    public sealed class SafeWebRtcEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeWebRtcEngineHandle() : base(ownsHandle: true) { }

        public SafeWebRtcEngineHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_webrtc_engine_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeWebRtcEngineHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native H.264 Hardware Encoder instances.
    /// </summary>
    public sealed class SafeEncoderHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeEncoderHandle() : base(ownsHandle: true) { }

        public SafeEncoderHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_encoder_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeEncoderHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native Output instances (RTMP, WebRTC, Callback).
    /// </summary>
    public sealed class SafeOutputHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeOutputHandle() : base(ownsHandle: true) { }

        public SafeOutputHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_output_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeOutputHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native MediaFrame instances.
    /// </summary>
    public sealed class SafeMediaFrameHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeMediaFrameHandle() : base(ownsHandle: true) { }

        public SafeMediaFrameHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_media_frame_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeMediaFrameHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native Playlist instances.
    /// </summary>
    public sealed class SafePlaylistHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePlaylistHandle() : base(ownsHandle: true) { }

        public SafePlaylistHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_playlist_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafePlaylistHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }

    /// <summary>
    /// Type-safe SafeHandle for native CG Engine instances.
    /// </summary>
    public sealed class SafeCgEngineHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeCgEngineHandle() : base(ownsHandle: true) { }

        public SafeCgEngineHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(ownsHandle)
        {
            SetHandle(preexistingHandle);
        }

        protected override bool ReleaseHandle()
        {
            NativeBridge.ome_cg_engine_destroy_internal(handle);
            return true;
        }

        public static implicit operator IntPtr(SafeCgEngineHandle? handle) => handle?.DangerousGetHandle() ?? IntPtr.Zero;
    }
}
