import { Registry, Gauge, collectDefaultMetrics } from 'prom-client';

export class MetricsRegistry {
  public readonly register: Registry;

  // Camera stream metrics
  public readonly cameraBitrate: Gauge<string>;
  public readonly cameraFps: Gauge<string>;
  public readonly cameraPacketLoss: Gauge<string>;
  public readonly cameraRtt: Gauge<string>;
  public readonly cameraJitter: Gauge<string>;
  public readonly cameraDroppedFrames: Gauge<string>;
  public readonly cameraOnline: Gauge<string>;
  public readonly cameraTally: Gauge<string>;

  // Aggregated network throughput
  public readonly wanThroughput: Gauge<string>;

  // Hardware / Host resources
  public readonly systemCpu: Gauge<string>;
  public readonly systemGpu: Gauge<string>;
  public readonly systemRam: Gauge<string>;

  // Component health
  public readonly componentHealth: Gauge<string>;

  constructor() {
    this.register = new Registry();

    // Collect default Node.js runtime metrics
    collectDefaultMetrics({ register: this.register, prefix: 'broadcast_monitor_' });

    this.cameraBitrate = new Gauge({
      name: 'webrtc_camera_bitrate_kbps',
      help: 'Current video bitrate per camera in kbps',
      labelNames: ['camera_id', 'codec', 'resolution'],
      registers: [this.register],
    });

    this.cameraFps = new Gauge({
      name: 'webrtc_camera_fps',
      help: 'Current frame rate per camera in frames per second',
      labelNames: ['camera_id'],
      registers: [this.register],
    });

    this.cameraPacketLoss = new Gauge({
      name: 'webrtc_camera_packet_loss_ratio',
      help: 'Packet loss ratio between 0.0 and 1.0 per camera',
      labelNames: ['camera_id'],
      registers: [this.register],
    });

    this.cameraRtt = new Gauge({
      name: 'webrtc_camera_rtt_seconds',
      help: 'Round-trip time in seconds per camera',
      labelNames: ['camera_id'],
      registers: [this.register],
    });

    this.cameraJitter = new Gauge({
      name: 'webrtc_camera_jitter_seconds',
      help: 'Jitter buffer latency in seconds per camera',
      labelNames: ['camera_id'],
      registers: [this.register],
    });

    this.cameraDroppedFrames = new Gauge({
      name: 'webrtc_camera_dropped_frames_total',
      help: 'Total dropped frames count per camera',
      labelNames: ['camera_id'],
      registers: [this.register],
    });

    this.cameraOnline = new Gauge({
      name: 'webrtc_camera_online_status',
      help: 'Camera online status (1 = online, 0 = offline)',
      labelNames: ['camera_id'],
      registers: [this.register],
    });

    this.cameraTally = new Gauge({
      name: 'webrtc_camera_tally_state',
      help: 'Camera Tally state (2 = on-air, 1 = preview, 0 = off)',
      labelNames: ['camera_id'],
      registers: [this.register],
    });

    this.wanThroughput = new Gauge({
      name: 'webrtc_wan_bandwidth_total_kbps',
      help: 'Aggregated WAN bandwidth consumption in kbps',
      labelNames: ['direction'], // tx, rx
      registers: [this.register],
    });

    this.systemCpu = new Gauge({
      name: 'webrtc_system_cpu_usage_ratio',
      help: 'CPU usage ratio between 0.0 and 1.0',
      labelNames: ['machine_id', 'source'],
      registers: [this.register],
    });

    this.systemGpu = new Gauge({
      name: 'webrtc_system_gpu_usage_ratio',
      help: 'GPU utilization ratio between 0.0 and 1.0',
      labelNames: ['machine_id', 'source'],
      registers: [this.register],
    });

    this.systemRam = new Gauge({
      name: 'webrtc_system_ram_usage_ratio',
      help: 'RAM usage ratio between 0.0 and 1.0',
      labelNames: ['machine_id', 'source'],
      registers: [this.register],
    });

    this.componentHealth = new Gauge({
      name: 'webrtc_component_health_status',
      help: 'Component health status (1 = healthy, 0.5 = degraded, 0 = unhealthy)',
      labelNames: ['component'],
      registers: [this.register],
    });
  }
}

export const metricsRegistry = new MetricsRegistry();
