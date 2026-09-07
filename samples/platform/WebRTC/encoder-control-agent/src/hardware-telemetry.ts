import os from 'os';
import { SystemMetrics, StreamMetrics } from '@webrtc-broadcast/shared';

export class HardwareTelemetry {
  private lastCpuUsage: NodeJS.CpuUsage | null = null;
  private lastCpuTime = Date.now();
  private prevTxBytes = 0;
  private prevTime = Date.now();

  constructor(private machineId: string, private cameraId: string) {}

  public sampleSystemMetrics(): SystemMetrics {
    const memTotal = os.totalmem();
    const memFree = os.freemem();
    const ramUsage = Math.round(((memTotal - memFree) / memTotal) * 100);

    // Compute CPU percentage
    let cpuPercent = 20;
    const cpus = os.cpus();
    if (cpus.length > 0) {
      let totalIdle = 0;
      let totalTick = 0;
      for (const cpu of cpus) {
        for (const type in cpu.times) {
          totalTick += (cpu.times as any)[type];
        }
        totalIdle += cpu.times.idle;
      }
      const idleRatio = totalIdle / totalTick;
      cpuPercent = Math.min(100, Math.max(5, Math.round((1 - idleRatio) * 100)));
    }

    // Simulated GPU load for hardware encoding
    const gpuPercent = Math.min(100, Math.max(10, Math.round(cpuPercent * 0.8 + 15)));

    return {
      machineId: this.machineId,
      cpu: cpuPercent,
      gpu: gpuPercent,
      ram: ramUsage,
      temperature: 48 + Math.round(Math.sin(Date.now() / 10000) * 4),
      networkThroughput: {
        txKbps: 8500 + Math.round(Math.random() * 200),
        rxKbps: 120 + Math.round(Math.random() * 20),
      },
      diskUsage: 35,
      timestamp: Date.now(),
    };
  }

  public sampleStreamMetrics(currentBitrate: number, fps: number): StreamMetrics {
    return {
      cameraId: this.cameraId,
      bitrate: currentBitrate,
      fps: fps,
      packetLoss: Number((Math.random() * 0.15).toFixed(2)),
      rtt: 16 + Math.round(Math.random() * 4),
      jitter: 2 + Math.round(Math.random() * 2),
      encoderLatency: 8 + Math.round(Math.random() * 3),
      encoderLoad: 42 + Math.round(Math.random() * 10),
      droppedFrames: 0,
      timestamp: Date.now(),
    };
  }
}
