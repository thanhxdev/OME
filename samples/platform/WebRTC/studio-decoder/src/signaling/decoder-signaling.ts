import WebSocket from 'ws';
import http from 'http';
import {
  SignalingMessage,
  CodecChangeMessage,
  TallyState,
  Logger,
  StreamMetrics,
} from '@webrtc-broadcast/shared';
import { DecoderOptions } from '../config';
import { DecoderRunner } from '../pipeline/decoder-runner';

const logger = new Logger('DecoderSignaling');

export class DecoderSignalingClient {
  private ws: WebSocket | null = null;
  private options: DecoderOptions;
  private runner: DecoderRunner;
  private metricsTimer: NodeJS.Timeout | null = null;
  private outputFps: number = 0;
  private droppedFrames: number = 0;

  constructor(options: DecoderOptions, runner: DecoderRunner) {
    this.options = options;
    this.runner = runner;

    this.runner.on('telemetry', (t) => {
      this.outputFps = t.fps;
      this.droppedFrames = t.droppedFrames;
    });
  }

  public async connect(): Promise<void> {
    // 1. Subscribe to SFU Media Server
    await this.subscribeToSfu();

    // 2. Connect to Signaling Server
    logger.info(`Connecting Decoder to Signaling Server at ${this.options.signalingUrl}...`);
    this.ws = new WebSocket(this.options.signalingUrl);

    this.ws.on('open', () => {
      logger.info(`✅ Decoder connected to Signaling Server for ${this.options.cameraId}`);

      this.send({
        type: 'join_session',
        sessionId: this.options.sessionId,
        senderId: `decoder-${this.options.cameraId}`,
        role: 'decoder',
        cameraId: this.options.cameraId,
        timestamp: Date.now(),
      });

      this.startHealthReporting();
    });

    this.ws.on('message', (raw: WebSocket.Data) => {
      try {
        const msg = JSON.parse(raw.toString()) as SignalingMessage;
        this.handleMessage(msg);
      } catch (err) {
        logger.error('Failed to parse signaling message in decoder', err);
      }
    });

    this.ws.on('close', () => {
      logger.warn('Decoder signaling disconnected. Reconnecting in 3s...');
      this.stopHealthReporting();
      setTimeout(() => this.connect(), 3000);
    });

    this.ws.on('error', (err) => {
      logger.error('Decoder signaling error', err);
    });
  }

  private handleMessage(msg: SignalingMessage): void {
    if (msg.type === 'codec_change') {
      const c = msg as CodecChangeMessage;
      if (c.cameraId === this.options.cameraId || c.cameraId === 'all') {
        logger.info(`Codec change event received: switching to ${c.codec}`);
        this.runner.switchCodec(c.codec);
      }
    }
  }

  /**
   * Registers this decoder instance as an active subscriber on the SFU
   */
  private subscribeToSfu(): Promise<void> {
    return new Promise((resolve) => {
      const url = new URL(`${this.options.sfuApiUrl}/streams/${this.options.cameraId}/subscribe`);
      const body = JSON.stringify({
        id: `decoder-${this.options.cameraId}`,
        role: 'decoder',
        ip: '127.0.0.1',
        videoPort: this.options.listenVideoPort,
        audioPort: this.options.listenAudioPort,
      });

      const req = http.request(
        {
          hostname: url.hostname,
          port: url.port,
          path: url.pathname,
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Content-Length': Buffer.byteLength(body),
          },
        },
        (res) => {
          let data = '';
          res.on('data', (c) => (data += c));
          res.on('end', () => {
            logger.info(`Subscribed to SFU for ${this.options.cameraId}: ${data}`);
            resolve();
          });
        }
      );

      req.on('error', (err) => {
        logger.warn(`SFU subscribe request warning (SFU might start later): ${err.message}`);
        resolve(); // Continue anyway so decoder can reconnect
      });

      req.write(body);
      req.end();
    });
  }

  /**
   * Sends Tally status update back to signaling server (e.g. from ATEM switcher)
   */
  public sendTallyUpdate(state: TallyState): void {
    logger.info(`Broadcasting Tally Feedback: ${this.options.cameraId} -> ${state.toUpperCase()}`);
    this.send({
      type: 'tally_update',
      sessionId: this.options.sessionId,
      senderId: `decoder-${this.options.cameraId}`,
      cameraId: this.options.cameraId,
      state,
      timestamp: Date.now(),
    });
  }

  private startHealthReporting(): void {
    this.stopHealthReporting();
    this.metricsTimer = setInterval(() => {
      if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;

      const streamMetrics: StreamMetrics = {
        cameraId: this.options.cameraId,
        bitrate: 0,
        fps: this.outputFps,
        packetLoss: 0,
        rtt: 12,
        encoderLatency: this.options.bufferMs,
        droppedFrames: this.droppedFrames,
        timestamp: Date.now(),
      };

      this.send({
        type: 'health_report',
        sessionId: this.options.sessionId,
        senderId: `decoder-${this.options.cameraId}`,
        sourceId: this.options.cameraId,
        sourceType: 'decoder',
        streamMetrics,
        timestamp: Date.now(),
      });
    }, 1000);
  }

  private stopHealthReporting(): void {
    if (this.metricsTimer) {
      clearInterval(this.metricsTimer);
      this.metricsTimer = null;
    }
  }

  public send(msg: SignalingMessage): void {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }

  public close(): void {
    this.stopHealthReporting();
    if (this.ws) {
      this.ws.removeAllListeners();
      this.ws.close();
    }
  }
}
