import WebSocket from 'ws';
import {
  CameraStatus,
  SignalingMessage,
  StreamMetrics,
  SystemMetrics,
  TallyState,
  CodecMode,
} from '@webrtc-broadcast/shared';
import { config } from './config';
import { metricsRegistry } from './metrics-registry';

export interface CameraMetricsSummary {
  id: string;
  name: string;
  online: boolean;
  codec: CodecMode;
  bitrate: number;
  fps: number;
  packetLoss: number;
  rtt: number;
  jitter: number;
  droppedFrames: number;
  tally: TallyState;
  resolution: string;
  lastUpdated: number;
}

export interface SummaryReport {
  sessionId: string;
  timestamp: number;
  totalWanTxKbps: number;
  totalWanRxKbps: number;
  wanWarningThresholdKbps: number;
  wanLimitThresholdKbps: number;
  isWanOverloaded: boolean;
  cameras: CameraMetricsSummary[];
  systemMetrics: {
    cpu: number;
    gpu: number;
    ram: number;
  };
  components: Record<string, { status: string; uptimeSeconds: number }>;
}

export class TelemetryCollector {
  private ws: WebSocket | null = null;
  private isRunning = false;
  private reconnectTimer: NodeJS.Timeout | null = null;
  private scrapeTimer: NodeJS.Timeout | null = null;

  // In-memory state of cameras
  private cameras: Map<string, CameraMetricsSummary> = new Map();
  private systemState = { cpu: 0.15, gpu: 0.22, ram: 0.35 };
  private componentHealth: Record<string, { status: string; uptimeSeconds: number }> = {
    signaling: { status: 'healthy', uptimeSeconds: 0 },
    sfu: { status: 'healthy', uptimeSeconds: 0 },
    monitoring: { status: 'healthy', uptimeSeconds: 0 },
  };

  constructor() {
    // Initialize 10 default cameras (cam-01 through cam-10)
    for (let i = 1; i <= 10; i++) {
      const id = `cam-${String(i).padStart(2, '0')}`;
      this.cameras.set(id, {
        id,
        name: this.getDefaultCameraName(i),
        online: false,
        codec: 'h264',
        bitrate: 0,
        fps: 0,
        packetLoss: 0,
        rtt: 0,
        jitter: 0,
        droppedFrames: 0,
        tally: 'off',
        resolution: '1920x1080',
        lastUpdated: Date.now(),
      });
    }
  }

  private getDefaultCameraName(index: number): string {
    const names = [
      'Main Wide 50m',
      'Goal Box Left',
      'Goal Box Right',
      'Bench Home',
      'Bench Away',
      'Spidercam High',
      'Close-up Center',
      'Tactical Aerial',
      'Tunnel Entry',
      'Beauty Exterior',
    ];
    return names[index - 1] || `Camera ${index}`;
  }

  public start(): void {
    if (this.isRunning) return;
    this.isRunning = true;
    this.connectSignaling();
    this.startSfuScraping();
  }

  public stop(): void {
    this.isRunning = false;
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    if (this.scrapeTimer) clearInterval(this.scrapeTimer);
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }

  private connectSignaling(): void {
    if (!this.isRunning) return;

    try {
      this.ws = new WebSocket(config.signalingWsUrl);

      this.ws.on('open', () => {
        this.componentHealth.signaling = { status: 'healthy', uptimeSeconds: 0 };
        metricsRegistry.componentHealth.set({ component: 'signaling' }, 1.0);

        // Join session as monitor
        const joinMsg: SignalingMessage = {
          type: 'join_session',
          sessionId: config.sessionId,
          senderId: `monitor-${Date.now().toString(36)}`,
          timestamp: Date.now(),
          role: 'monitor',
        };
        this.ws?.send(JSON.stringify(joinMsg));
      });

      this.ws.on('message', (data: WebSocket.RawData) => {
        try {
          const msg = JSON.parse(data.toString()) as SignalingMessage;
          this.handleSignalingMessage(msg);
        } catch (e) {
          // Ignore parse errors
        }
      });

      this.ws.on('close', () => {
        this.componentHealth.signaling = { status: 'degraded', uptimeSeconds: 0 };
        metricsRegistry.componentHealth.set({ component: 'signaling' }, 0.5);
        this.scheduleReconnect();
      });

      this.ws.on('error', () => {
        this.componentHealth.signaling = { status: 'unhealthy', uptimeSeconds: 0 };
        metricsRegistry.componentHealth.set({ component: 'signaling' }, 0.0);
      });
    } catch (e) {
      this.scheduleReconnect();
    }
  }

  private scheduleReconnect(): void {
    if (!this.isRunning) return;
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = setTimeout(() => {
      this.connectSignaling();
    }, 3000);
  }

  private handleSignalingMessage(msg: SignalingMessage): void {
    switch (msg.type) {
      case 'session_sync':
      case 'joined_session': {
        const cameras = (msg as any).cameras as CameraStatus[] | undefined;
        if (Array.isArray(cameras)) {
          for (const cam of cameras) {
            this.updateCameraStatus(cam);
          }
        }
        break;
      }
      case 'tally_update': {
        const cam = this.cameras.get(msg.cameraId);
        if (cam) {
          cam.tally = msg.state;
          cam.lastUpdated = Date.now();
          const tallyVal = msg.state === 'on-air' ? 2 : msg.state === 'preview' ? 1 : 0;
          metricsRegistry.cameraTally.set({ camera_id: msg.cameraId }, tallyVal);
        }
        break;
      }
      case 'codec_changed':
      case 'codec_change': {
        const target = msg.cameraId;
        if (target === 'all') {
          for (const [id, cam] of this.cameras.entries()) {
            cam.codec = msg.codec;
            if (msg.bitrate) cam.bitrate = msg.bitrate;
            if (msg.fps) cam.fps = msg.fps;
            if (msg.resolution) cam.resolution = msg.resolution;
            cam.lastUpdated = Date.now();
          }
        } else {
          const cam = this.cameras.get(target);
          if (cam) {
            cam.codec = msg.codec;
            if (msg.bitrate) cam.bitrate = msg.bitrate;
            if (msg.fps) cam.fps = msg.fps;
            if (msg.resolution) cam.resolution = msg.resolution;
            cam.lastUpdated = Date.now();
          }
        }
        this.updatePrometheusGauges();
        break;
      }
      case 'health_report': {
        if (msg.streamMetrics) {
          this.applyStreamMetrics(msg.streamMetrics);
        }
        if (msg.systemMetrics) {
          this.applySystemMetrics(msg.systemMetrics);
        }
        break;
      }
    }
  }

  public updateCameraStatus(status: CameraStatus): void {
    let cam = this.cameras.get(status.id);
    if (!cam) {
      cam = {
        id: status.id,
        name: status.name || status.id,
        online: status.online,
        codec: status.codec,
        bitrate: status.currentBitrate,
        fps: status.fps,
        packetLoss: status.packetLoss,
        rtt: status.rtt,
        jitter: 0,
        droppedFrames: 0,
        tally: status.tally,
        resolution: '1920x1080',
        lastUpdated: Date.now(),
      };
      this.cameras.set(status.id, cam);
    } else {
      cam.online = status.online;
      cam.codec = status.codec;
      cam.bitrate = status.currentBitrate;
      cam.fps = status.fps;
      cam.packetLoss = status.packetLoss;
      cam.rtt = status.rtt;
      cam.tally = status.tally;
      if (status.name) cam.name = status.name;
      cam.lastUpdated = Date.now();
    }
    this.updatePrometheusGauges();
  }

  public applyStreamMetrics(m: StreamMetrics): void {
    let cam = this.cameras.get(m.cameraId);
    if (!cam) {
      cam = {
        id: m.cameraId,
        name: m.cameraId,
        online: true,
        codec: 'h264',
        bitrate: m.bitrate,
        fps: m.fps,
        packetLoss: m.packetLoss,
        rtt: m.rtt,
        jitter: m.jitter || 0,
        droppedFrames: m.droppedFrames || 0,
        tally: 'off',
        resolution: '1920x1080',
        lastUpdated: Date.now(),
      };
      this.cameras.set(m.cameraId, cam);
    } else {
      cam.online = true;
      cam.bitrate = m.bitrate;
      cam.fps = m.fps;
      cam.packetLoss = m.packetLoss;
      cam.rtt = m.rtt;
      if (m.jitter !== undefined) cam.jitter = m.jitter;
      if (m.droppedFrames !== undefined) cam.droppedFrames = m.droppedFrames;
      cam.lastUpdated = Date.now();
    }

    metricsRegistry.cameraBitrate.set(
      { camera_id: cam.id, codec: cam.codec, resolution: cam.resolution },
      cam.bitrate
    );
    metricsRegistry.cameraFps.set({ camera_id: cam.id }, cam.fps);
    metricsRegistry.cameraPacketLoss.set({ camera_id: cam.id }, cam.packetLoss / 100);
    metricsRegistry.cameraRtt.set({ camera_id: cam.id }, cam.rtt / 1000);
    metricsRegistry.cameraJitter.set({ camera_id: cam.id }, cam.jitter / 1000);
    metricsRegistry.cameraDroppedFrames.set({ camera_id: cam.id }, cam.droppedFrames);
    metricsRegistry.cameraOnline.set({ camera_id: cam.id }, 1);

    this.recalculateTotalWan();
  }

  public applySystemMetrics(s: SystemMetrics): void {
    this.systemState.cpu = s.cpu / 100;
    if (s.gpu !== undefined) this.systemState.gpu = s.gpu / 100;
    this.systemState.ram = s.ram / 100;

    metricsRegistry.systemCpu.set({ machine_id: s.machineId, source: 'host' }, s.cpu / 100);
    if (s.gpu !== undefined) {
      metricsRegistry.systemGpu.set({ machine_id: s.machineId, source: 'host' }, s.gpu / 100);
    }
    metricsRegistry.systemRam.set({ machine_id: s.machineId, source: 'host' }, s.ram / 100);
  }

  private startSfuScraping(): void {
    this.scrapeTimer = setInterval(async () => {
      try {
        const response = await fetch(`${config.sfuHttpUrl}/api/streams`);
        if (response.ok) {
          const data = (await response.json()) as any;
          this.componentHealth.sfu = { status: 'healthy', uptimeSeconds: data.uptime || 0 };
          metricsRegistry.componentHealth.set({ component: 'sfu' }, 1.0);

          if (Array.isArray(data.streams)) {
            for (const s of data.streams) {
              const cam = this.cameras.get(s.id);
              if (cam) {
                cam.online = s.active !== false;
                if (s.bitrate) cam.bitrate = s.bitrate;
                if (s.fps) cam.fps = s.fps;
                if (s.codec) cam.codec = s.codec;
                cam.lastUpdated = Date.now();
              }
            }
          }
          this.updatePrometheusGauges();
        } else {
          this.componentHealth.sfu = { status: 'degraded', uptimeSeconds: 0 };
          metricsRegistry.componentHealth.set({ component: 'sfu' }, 0.5);
        }
      } catch (err) {
        this.componentHealth.sfu = { status: 'unhealthy', uptimeSeconds: 0 };
        metricsRegistry.componentHealth.set({ component: 'sfu' }, 0.0);
      }
    }, config.scrapeIntervalMs);
  }

  private updatePrometheusGauges(): void {
    for (const cam of this.cameras.values()) {
      metricsRegistry.cameraOnline.set({ camera_id: cam.id }, cam.online ? 1 : 0);
      const tallyVal = cam.tally === 'on-air' ? 2 : cam.tally === 'preview' ? 1 : 0;
      metricsRegistry.cameraTally.set({ camera_id: cam.id }, tallyVal);

      if (cam.online) {
        metricsRegistry.cameraBitrate.set(
          { camera_id: cam.id, codec: cam.codec, resolution: cam.resolution },
          cam.bitrate
        );
        metricsRegistry.cameraFps.set({ camera_id: cam.id }, cam.fps);
        metricsRegistry.cameraPacketLoss.set({ camera_id: cam.id }, cam.packetLoss / 100);
        metricsRegistry.cameraRtt.set({ camera_id: cam.id }, cam.rtt / 1000);
      }
    }
    this.recalculateTotalWan();
  }

  private recalculateTotalWan(): void {
    let totalTx = 0;
    for (const cam of this.cameras.values()) {
      if (cam.online) {
        totalTx += cam.bitrate;
      }
    }
    metricsRegistry.wanThroughput.set({ direction: 'tx' }, totalTx);
    metricsRegistry.wanThroughput.set({ direction: 'rx' }, Math.round(totalTx * 0.98));
  }

  public getSummary(): SummaryReport {
    const cameraList = Array.from(this.cameras.values()).sort((a, b) => a.id.localeCompare(b.id));
    const totalTx = cameraList.reduce((acc, c) => acc + (c.online ? c.bitrate : 0), 0);
    const totalRx = Math.round(totalTx * 0.98);

    return {
      sessionId: config.sessionId,
      timestamp: Date.now(),
      totalWanTxKbps: totalTx,
      totalWanRxKbps: totalRx,
      wanWarningThresholdKbps: config.wanBandwidthWarningKbps,
      wanLimitThresholdKbps: config.wanBandwidthLimitKbps,
      isWanOverloaded: totalTx >= config.wanBandwidthWarningKbps,
      cameras: cameraList,
      systemMetrics: {
        cpu: Math.round(this.systemState.cpu * 100),
        gpu: Math.round(this.systemState.gpu * 100),
        ram: Math.round(this.systemState.ram * 100),
      },
      components: this.componentHealth,
    };
  }
}
