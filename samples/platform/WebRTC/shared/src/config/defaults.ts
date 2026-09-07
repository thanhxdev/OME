export const DEFAULT_PORTS = {
  SIGNALING_HTTP: 3000,
  SIGNALING_WS: 3001,
  MEDIA_SERVER_HTTP: 4000,
  MEDIA_SERVER_RTC_MIN: 10000,
  MEDIA_SERVER_RTC_MAX: 10100,
  TURN_STUN_UDP_TCP: 3478,
  TURN_TLS: 5349,
  ENCODER_CONTROL_AGENT: 8080,
  MONITORING_COLLECTOR: 9090,
  PROMETHEUS: 9091,
  GRAFANA: 3005,
  ALERTMANAGER: 9093,
  RECORDING_SERVICE: 5000,
  MONITOR_WALL: 5173,
  CONTROL_DASHBOARD: 5174,
} as const;

export const DEFAULT_ICE_SERVERS: RTCIceServer[] = [
  {
    urls: ['stun:stun.l.google.com:19302', 'stun:stun1.l.google.com:19302'],
  },
  {
    urls: ['turn:localhost:3478?transport=udp', 'turn:localhost:3478?transport=tcp'],
    username: 'broadcast_user',
    credential: 'broadcast_password',
  },
];

export const DEFAULT_API_ENDPOINTS = {
  SESSIONS: '/api/sessions',
  CAMERAS: '/api/cameras',
  AUTH_TOKEN: '/api/auth/token',
  HEALTH: '/api/health',
  METRICS: '/metrics',
} as const;

export const BROADCAST_LIMITS = {
  MAX_CAMERAS: 10,
  DEFAULT_VIDEO_BITRATE_KBPS: 10000, // 10 Mbps for 1080p60
  MIN_VIDEO_BITRATE_KBPS: 1500,
  MAX_VIDEO_BITRATE_KBPS: 25000,
  DEFAULT_AUDIO_BITRATE_KBPS: 128,
  DEFAULT_JITTER_BUFFER_MS: 60,
  HEALTH_REPORT_INTERVAL_MS: 1000,
  PRESENCE_HEARTBEAT_INTERVAL_MS: 5000,
  RECORDING_SEGMENT_DURATION_SECONDS: 1800, // 30 minutes
} as const;
