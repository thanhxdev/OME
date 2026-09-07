export type CodecMode = 'throughpass' | 'h264' | 'h265';

export interface CodecConfig {
  mode: CodecMode;
  bitrate?: number; // in kbps (e.g. 10000 = 10 Mbps)
  resolution?: string; // e.g. "1920x1080", "1280x720"
  fps?: number; // e.g. 60, 50, 30, 25
  profile?: string; // e.g. "high", "main"
  preset?: string; // e.g. "zerolatency", "ultrafast"
  rateControl?: 'cbr' | 'vbr';
}

export type PresetName = 'max-quality' | 'balanced' | 'bandwidth-saver' | 'minimal';

export const CODEC_PRESETS: Record<PresetName, CodecConfig> = {
  'max-quality': {
    mode: 'throughpass',
  },
  'balanced': {
    mode: 'h264',
    bitrate: 10000,
    resolution: '1920x1080',
    fps: 60,
    profile: 'high',
    preset: 'zerolatency',
    rateControl: 'cbr',
  },
  'bandwidth-saver': {
    mode: 'h265',
    bitrate: 6000,
    resolution: '1920x1080',
    fps: 30,
    profile: 'main',
    preset: 'zerolatency',
    rateControl: 'cbr',
  },
  'minimal': {
    mode: 'h265',
    bitrate: 3000,
    resolution: '1280x720',
    fps: 25,
    profile: 'main',
    preset: 'zerolatency',
    rateControl: 'cbr',
  },
};

export const CODEC_PAYLOAD_TYPES = {
  H264: 96,
  H265: 97, // Custom broadcast RTP payload type
  OPUS: 111,
} as const;
