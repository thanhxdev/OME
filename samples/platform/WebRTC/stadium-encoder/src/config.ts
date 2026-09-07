import { CodecMode, InputType, DEFAULT_PORTS } from '@webrtc-broadcast/shared';

export interface EncoderOptions {
  cameraId: string;
  cameraName: string;
  inputType: InputType | 'testpattern';
  deviceIndex: number;
  codec: CodecMode;
  bitrate: number; // kbps
  resolution: string; // e.g. "1920x1080"
  fps: number;
  signalingUrl: string;
  sessionId: string;
  sfuHost: string;
  sfuVideoPort: number;
  sfuAudioPort: number;
  turnUrl?: string;
  turnUser?: string;
  turnPass?: string;
  hwAccel?: 'nvenc' | 'qsv' | 'cpu';
}

export function getDefaultOptions(): EncoderOptions {
  return {
    cameraId: 'cam-01',
    cameraName: 'Main Wide',
    inputType: 'testpattern',
    deviceIndex: 0,
    codec: 'h264',
    bitrate: 10000,
    resolution: '1920x1080',
    fps: 60,
    signalingUrl: `ws://localhost:${DEFAULT_PORTS.SIGNALING_HTTP}/ws`,
    sessionId: 'event-001',
    sfuHost: '127.0.0.1',
    sfuVideoPort: DEFAULT_PORTS.MEDIA_SERVER_RTC_MIN, // 10000
    sfuAudioPort: DEFAULT_PORTS.MEDIA_SERVER_RTC_MIN + 2, // 10002
    hwAccel: 'nvenc',
  };
}
