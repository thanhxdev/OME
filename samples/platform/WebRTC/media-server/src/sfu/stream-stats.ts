export interface LiveStreamStats {
  cameraId: string;
  active: boolean;
  codec: string;
  ingressPort: number;
  subscribersCount: number;
  bytesReceived: number;
  packetsReceived: number;
  packetsForwarded: number;
  currentBitrateKbps: number;
  currentFps: number;
  lastKeyframeTime: number;
  lastPacketTime: number;
}

export class StreamStatsTracker {
  private stats: Map<string, LiveStreamStats> = new Map();
  private lastSample: Map<string, { bytes: number; packets: number; time: number }> = new Map();

  public registerStream(cameraId: string, ingressPort: number, codec: string = 'h264'): void {
    const initial: LiveStreamStats = {
      cameraId,
      active: false,
      codec,
      ingressPort,
      subscribersCount: 0,
      bytesReceived: 0,
      packetsReceived: 0,
      packetsForwarded: 0,
      currentBitrateKbps: 0,
      currentFps: 0,
      lastKeyframeTime: 0,
      lastPacketTime: 0,
    };
    this.stats.set(cameraId, initial);
    this.lastSample.set(cameraId, { bytes: 0, packets: 0, time: Date.now() });
  }

  public recordPacket(cameraId: string, bytes: number, isKeyframe: boolean = false): void {
    const s = this.stats.get(cameraId);
    if (!s) return;

    s.active = true;
    s.bytesReceived += bytes;
    s.packetsReceived += 1;
    s.lastPacketTime = Date.now();
    if (isKeyframe) {
      s.lastKeyframeTime = Date.now();
    }
  }

  public recordForward(cameraId: string, count: number = 1): void {
    const s = this.stats.get(cameraId);
    if (s) {
      s.packetsForwarded += count;
    }
  }

  public updateSubscriberCount(cameraId: string, count: number): void {
    const s = this.stats.get(cameraId);
    if (s) {
      s.subscribersCount = count;
    }
  }

  public setCodec(cameraId: string, codec: string): void {
    const s = this.stats.get(cameraId);
    if (s) {
      s.codec = codec;
    }
  }

  public calculatePeriodicRates(): void {
    const now = Date.now();
    for (const [cameraId, s] of this.stats.entries()) {
      const prev = this.lastSample.get(cameraId);
      if (!prev) continue;

      const elapsedSec = (now - prev.time) / 1000;
      if (elapsedSec > 0.5) {
        const deltaBytes = s.bytesReceived - prev.bytes;
        const deltaPackets = s.packetsReceived - prev.packets;

        // Bitrate in kbps (bytes * 8 / 1000 / elapsedSec)
        s.currentBitrateKbps = Math.round((deltaBytes * 8) / 1000 / elapsedSec);
        // Estimate fps (~30-60 packets per second assuming ~1 pkt per frame on low motion or ~2-4 per frame)
        s.currentFps = Math.min(60, Math.round(deltaPackets / elapsedSec / 1.5));

        if (now - s.lastPacketTime > 3000) {
          s.active = false;
          s.currentBitrateKbps = 0;
          s.currentFps = 0;
        }

        this.lastSample.set(cameraId, { bytes: s.bytesReceived, packets: s.packetsReceived, time: now });
      }
    }
  }

  public getStats(cameraId: string): LiveStreamStats | undefined {
    return this.stats.get(cameraId);
  }

  public getAllStats(): LiveStreamStats[] {
    return Array.from(this.stats.values());
  }
}
