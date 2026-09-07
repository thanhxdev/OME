import { CodecConfig, CodecMode } from './codec';

export const MAX_CAMERAS = 10;

export type InputType = 'sdi' | 'hdmi';

export interface CameraConfig {
  id: string; // e.g. "cam-01"
  name: string; // e.g. "Main Wide", "Goal Left"
  inputType: InputType; // "sdi" | "hdmi"
  deviceIndex: number; // Device card index e.g. 0, 1
  codec: CodecMode; // "throughpass" | "h264" | "h265"
  bitrate?: number; // kbps
  resolution: string; // "1920x1080", "1280x720"
  fps: number; // 60, 50, 30, 25
  customCodecConfig?: Partial<CodecConfig>;
}

export type TallyState = 'on-air' | 'preview' | 'off';

export interface CameraStatus {
  id: string;
  name?: string;
  online: boolean;
  codec: CodecMode;
  currentBitrate: number; // kbps
  packetLoss: number; // percentage 0.0 - 100.0
  rtt: number; // milliseconds
  fps: number;
  tally: TallyState;
  isRecording?: boolean;
  lastSeen?: number; // unix timestamp ms
}
