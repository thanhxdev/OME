import { DEFAULT_PORTS, CodecMode } from '@webrtc-broadcast/shared';

export type OutputType = 'ndi' | 'sdi' | 'display' | 'null';

export interface DecoderOptions {
  cameraId: string;
  output: OutputType;
  ndiName?: string;
  sdiDeviceIndex?: number;
  signalingUrl: string;
  sessionId: string;
  sfuApiUrl: string;
  listenVideoPort: number;
  listenAudioPort: number;
  bufferMs: number; // Jitter buffer in milliseconds (40-100ms)
  activeCodec: CodecMode;
  hwAccel?: 'nvdec' | 'qsv' | 'cpu';
}

export function getDefaultDecoderOptions(): DecoderOptions {
  return {
    cameraId: 'cam-01',
    output: 'display',
    ndiName: 'CAM 01 - Main Wide',
    sdiDeviceIndex: 0,
    signalingUrl: `ws://localhost:${DEFAULT_PORTS.SIGNALING_HTTP}/ws`,
    sessionId: 'event-001',
    sfuApiUrl: `http://localhost:${DEFAULT_PORTS.MEDIA_SERVER_HTTP}/api`,
    listenVideoPort: 20000,
    listenAudioPort: 20002,
    bufferMs: 60, // 60ms broadcast jitter buffer
    activeCodec: 'h264',
    hwAccel: 'cpu',
  };
}
