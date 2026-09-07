import dotenv from 'dotenv';
import path from 'path';
import { CodecMode } from '@webrtc-broadcast/shared';
dotenv.config();

export interface AgentConfig {
  cameraId: string;
  cameraName: string;
  inputType: 'sdi' | 'hdmi';
  deviceIndex: number;
  codec: CodecMode;
  bitrate: number;
  fps: number;
  resolution: string;
  signalingWsUrl: string;
  sessionId: string;
  sfuHost: string;
  sfuVideoPort: number;
  sfuAudioPort: number;
  hwAccel: 'nvenc' | 'qsv' | 'vaapi' | 'cpu';
  encoderScriptPath: string;
  telemetryIntervalMs: number;
  gpioOnAirPin: number;
  gpioPreviewPin: number;
  mockHardware: boolean;
}

export const defaultConfig: AgentConfig = {
  cameraId: process.env.CAMERA_ID || 'cam-01',
  cameraName: process.env.CAMERA_NAME || 'Main Field 1',
  inputType: (process.env.INPUT_TYPE || 'sdi') as 'sdi' | 'hdmi',
  deviceIndex: parseInt(process.env.DEVICE_INDEX || '0', 10),
  codec: (process.env.CODEC || 'h264') as CodecMode,
  bitrate: parseInt(process.env.BITRATE || '8500', 10),
  fps: parseInt(process.env.FPS || '60', 10),
  resolution: process.env.RESOLUTION || '1920x1080',
  signalingWsUrl: process.env.SIGNALING_WS_URL || 'ws://localhost:3000/ws',
  sessionId: process.env.SESSION_ID || 'event-001',
  sfuHost: process.env.SFU_HOST || '127.0.0.1',
  sfuVideoPort: parseInt(process.env.SFU_VIDEO_PORT || '10000', 10),
  sfuAudioPort: parseInt(process.env.SFU_AUDIO_PORT || '10002', 10),
  hwAccel: (process.env.HW_ACCEL || 'cpu') as 'nvenc' | 'qsv' | 'vaapi' | 'cpu',
  encoderScriptPath:
    process.env.ENCODER_SCRIPT_PATH ||
    path.resolve(__dirname, '../../stadium-encoder/dist/cli.js'),
  telemetryIntervalMs: parseInt(process.env.TELEMETRY_INTERVAL_MS || '2000', 10),
  gpioOnAirPin: parseInt(process.env.GPIO_ONAIR_PIN || '17', 10),
  gpioPreviewPin: parseInt(process.env.GPIO_PREVIEW_PIN || '27', 10),
  mockHardware: process.env.MOCK_HARDWARE !== 'false',
};
