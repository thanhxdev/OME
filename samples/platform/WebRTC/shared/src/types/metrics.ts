export interface StreamMetrics {
  cameraId: string;
  bitrate: number; // kbps
  fps: number;
  packetLoss: number; // percentage
  jitter?: number; // ms
  rtt: number; // ms
  encoderLatency?: number; // ms
  encoderLoad?: number; // percentage
  droppedFrames?: number;
  timestamp: number;
}

export interface SystemMetrics {
  machineId: string;
  cpu: number; // percentage 0-100
  gpu?: number; // percentage 0-100
  ram: number; // percentage 0-100
  temperature?: number; // Celsius
  networkThroughput: {
    txKbps: number;
    rxKbps: number;
  };
  diskUsage?: number; // percentage 0-100
  timestamp: number;
}

export interface ComponentHealth {
  component: 'signaling' | 'sfu' | 'turn' | 'encoder' | 'decoder' | 'recording' | 'monitoring';
  status: 'healthy' | 'degraded' | 'unhealthy';
  uptimeSeconds: number;
  message?: string;
  timestamp: number;
}
