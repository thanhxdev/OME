import dgram from 'dgram';
import { EventEmitter } from 'events';
import { Logger } from '@webrtc-broadcast/shared';
import { CONFIG } from '../config';
import { KeyframeDetector } from './keyframe-detector';
import { StreamStatsTracker, LiveStreamStats } from './stream-stats';

const logger = new Logger('RtpRouter', CONFIG.LOG_LEVEL);

export interface SubscriberEndpoint {
  id: string;
  role: string;
  ip: string;
  videoPort: number;
  audioPort?: number;
  joinedAt: number;
}

export interface CameraProducer {
  cameraId: string;
  videoPort: number;
  audioPort: number;
  videoSocket: dgram.Socket;
  audioSocket: dgram.Socket;
  subscribers: Map<string, SubscriberEndpoint>;
  codec: string;
  isSinglePort?: boolean;
}

export class RtpRouter extends EventEmitter {
  private producers: Map<string, CameraProducer> = new Map(); // cameraId -> Producer
  public statsTracker: StreamStatsTracker = new StreamStatsTracker();
  private basePort: number = CONFIG.RTC_MIN_PORT;

  constructor() {
    super();

    // Periodic rate calculation
    setInterval(() => {
      this.statsTracker.calculatePeriodicRates();
    }, 1000);
  }

  /**
   * Initializes ingress UDP listener ports for a camera (Video + Audio)
   */
  public registerCamera(cameraId: string, codec: string = 'h264'): CameraProducer {
    if (this.producers.has(cameraId)) {
      return this.producers.get(cameraId)!;
    }

    const camIndex = this.producers.size;
    const videoPort = this.basePort + camIndex * 4;
    const audioPort = videoPort + 2;

    const videoSocket = dgram.createSocket({ type: 'udp4', reuseAddr: true });
    const audioSocket = dgram.createSocket({ type: 'udp4', reuseAddr: true });

    const producer: CameraProducer = {
      cameraId,
      videoPort,
      audioPort,
      videoSocket,
      audioSocket,
      subscribers: new Map(),
      codec,
    };

    this.statsTracker.registerStream(cameraId, videoPort, codec);

    // Bind Video Socket
    videoSocket.on('error', (err) => {
      logger.error(`Video socket error on camera ${cameraId} (port ${videoPort})`, err);
    });

    videoSocket.on('message', (msg: Buffer, _rinfo: dgram.RemoteInfo) => {
      this.handleVideoPacket(producer, msg);
    });

    videoSocket.bind(videoPort, '0.0.0.0', () => {
      logger.info(`Camera ${cameraId} Video RTP listening on UDP ${videoPort}`);
    });

    // Bind Audio Socket
    audioSocket.on('error', (err) => {
      logger.error(`Audio socket error on camera ${cameraId} (port ${audioPort})`, err);
    });

    audioSocket.on('message', (msg: Buffer, _rinfo: dgram.RemoteInfo) => {
      this.handleAudioPacket(producer, msg);
    });

    audioSocket.bind(audioPort, '0.0.0.0', () => {
      logger.info(`Camera ${cameraId} Audio RTP listening on UDP ${audioPort}`);
    });

    this.producers.set(cameraId, producer);
    return producer;
  }

  /**
   * Finds an available UDP port, optionally trying preferredPort first, then searching range, then OS ephemeral (0).
   */
  public async allocateAvailableUdpPort(preferredPort?: number): Promise<number> {
    const isPortInUseByProducer = (port: number) => {
      for (const p of this.producers.values()) {
        if (p.videoPort === port || p.audioPort === port) return true;
      }
      return false;
    };

    const tryBind = (port: number): Promise<number> => {
      return new Promise((resolve, reject) => {
        const socket = dgram.createSocket({ type: 'udp4', reuseAddr: false });
        socket.once('error', (err) => {
          try { socket.close(); } catch {}
          reject(err);
        });
        socket.bind(port, '0.0.0.0', () => {
          const boundPort = socket.address().port;
          socket.close(() => {
            resolve(boundPort);
          });
        });
      });
    };

    // 1. Try preferred port if provided and not currently in use
    if (preferredPort && !isPortInUseByProducer(preferredPort)) {
      try {
        return await tryBind(preferredPort);
      } catch {}
    }

    // 2. Search within configured RTC range
    const minPort = CONFIG.RTC_MIN_PORT;
    const maxPort = CONFIG.RTC_MAX_PORT;
    for (let port = minPort; port <= maxPort; port++) {
      if (isPortInUseByProducer(port)) continue;
      try {
        return await tryBind(port);
      } catch {}
    }

    // 3. Fallback to OS ephemeral port
    return await tryBind(0);
  }

  /**
   * Dynamically allocates available UDP ingress ports for a camera (supports 1-Port BUNDLE and 2-Port Split)
   */
  public async allocateCamera(
    cameraId: string,
    isSinglePort: boolean,
    codec: string = 'h264'
  ): Promise<CameraProducer> {
    let existingSubscribers = new Map<string, SubscriberEndpoint>();
    if (this.producers.has(cameraId)) {
      const oldProducer = this.producers.get(cameraId)!;
      existingSubscribers = oldProducer.subscribers;
      try { oldProducer.videoSocket.close(); } catch {}
      if (oldProducer.audioSocket !== oldProducer.videoSocket) {
        try { oldProducer.audioSocket.close(); } catch {}
      }
      this.producers.delete(cameraId);
    }

    const videoPort = await this.allocateAvailableUdpPort();
    let audioPort = videoPort;
    if (!isSinglePort) {
      audioPort = await this.allocateAvailableUdpPort(videoPort + 2);
    }

    const videoSocket = dgram.createSocket({ type: 'udp4', reuseAddr: true });
    let audioSocket: dgram.Socket;

    if (isSinglePort) {
      audioSocket = videoSocket;
    } else {
      audioSocket = dgram.createSocket({ type: 'udp4', reuseAddr: true });
    }

    const producer: CameraProducer = {
      cameraId,
      videoPort,
      audioPort,
      videoSocket,
      audioSocket,
      subscribers: existingSubscribers,
      codec,
      isSinglePort,
    };

    this.statsTracker.registerStream(cameraId, videoPort, codec);

    videoSocket.on('error', (err) => {
      logger.error(`Video socket error on camera ${cameraId} (port ${videoPort})`, err);
    });

    videoSocket.on('message', (msg: Buffer) => {
      this.handleVideoPacket(producer, msg);
    });

    await new Promise<void>((resolve, reject) => {
      videoSocket.bind(videoPort, '0.0.0.0', () => {
        logger.info(`Camera ${cameraId} Video RTP (${isSinglePort ? 'BUNDLE' : 'SPLIT'}) listening on UDP ${videoPort}`);
        resolve();
      });
      videoSocket.once('error', reject);
    });

    if (!isSinglePort) {
      audioSocket.on('error', (err) => {
        logger.error(`Audio socket error on camera ${cameraId} (port ${audioPort})`, err);
      });

      audioSocket.on('message', (msg: Buffer) => {
        this.handleAudioPacket(producer, msg);
      });

      await new Promise<void>((resolve, reject) => {
        audioSocket.bind(audioPort, '0.0.0.0', () => {
          logger.info(`Camera ${cameraId} Audio RTP (SPLIT) listening on UDP ${audioPort}`);
          resolve();
        });
        audioSocket.once('error', reject);
      });
    }

    this.producers.set(cameraId, producer);
    return producer;
  }

  /**
   * Forwards incoming Video RTP packet to all active subscribers.
   */
  private handleVideoPacket(producer: CameraProducer, packet: Buffer): void {
    const isKeyframe =
      producer.codec === 'h265'
        ? KeyframeDetector.isH265Keyframe(packet)
        : KeyframeDetector.isH264Keyframe(packet);

    this.statsTracker.recordPacket(producer.cameraId, packet.length, isKeyframe);

    // Selective Streaming: only forward if subscribers exist
    if (producer.subscribers.size === 0) {
      return;
    }

    for (const sub of producer.subscribers.values()) {
      producer.videoSocket.send(packet, sub.videoPort, sub.ip, (err) => {
        if (err) {
          logger.warn(`Failed forwarding video packet to ${sub.id}@${sub.ip}:${sub.videoPort}`, { error: err.message });
        }
      });
    }

    this.statsTracker.recordForward(producer.cameraId, producer.subscribers.size);
  }

  /**
   * Forwards incoming Audio RTP packet to all active subscribers.
   */
  private handleAudioPacket(producer: CameraProducer, packet: Buffer): void {
    if (producer.subscribers.size === 0) return;

    for (const sub of producer.subscribers.values()) {
      if (sub.audioPort) {
        producer.audioSocket.send(packet, sub.audioPort, sub.ip);
      }
    }
  }

  /**
   * Adds a subscriber to a camera stream and emits PLI event to request immediate keyframe.
   */
  public addSubscriber(cameraId: string, subscriber: SubscriberEndpoint): void {
    let producer = this.producers.get(cameraId);
    if (!producer) {
      producer = this.registerCamera(cameraId);
    }

    producer.subscribers.set(subscriber.id, subscriber);
    this.statsTracker.updateSubscriberCount(cameraId, producer.subscribers.size);

    logger.info(`Subscriber ${subscriber.id} (${subscriber.role}) joined ${cameraId} -> ${subscriber.ip}:${subscriber.videoPort}`);

    // Request immediate keyframe / PLI for rapid playback startup
    this.emit('keyframe_needed', { cameraId, subscriberId: subscriber.id });
  }

  /**
   * Removes a subscriber from a camera feed.
   */
  public removeSubscriber(cameraId: string, subscriberId: string): void {
    const producer = this.producers.get(cameraId);
    if (producer) {
      producer.subscribers.delete(subscriberId);
      this.statsTracker.updateSubscriberCount(cameraId, producer.subscribers.size);
      logger.info(`Subscriber ${subscriberId} removed from ${cameraId}`);
    }
  }

  public setCameraCodec(cameraId: string, codec: string): void {
    const producer = this.producers.get(cameraId);
    if (producer) {
      producer.codec = codec;
      this.statsTracker.setCodec(cameraId, codec);
      logger.info(`Camera ${cameraId} router codec updated to ${codec}`);
    }
  }

  public getProducer(cameraId: string): CameraProducer | undefined {
    return this.producers.get(cameraId);
  }

  public getAllProducers(): CameraProducer[] {
    return Array.from(this.producers.values());
  }

  public close(): void {
    for (const p of this.producers.values()) {
      try { p.videoSocket.close(); } catch {}
      if (p.audioSocket !== p.videoSocket) {
        try { p.audioSocket.close(); } catch {}
      }
    }
    this.producers.clear();
  }
}
