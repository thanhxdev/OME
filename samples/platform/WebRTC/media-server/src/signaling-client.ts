import WebSocket from 'ws';
import {
  SignalingMessage,
  CodecChangeMessage,
  PresenceUpdateMessage,
  Logger,
} from '@webrtc-broadcast/shared';
import { CONFIG } from './config';
import { RtpRouter } from './sfu/rtp-router';

const logger = new Logger('SfuSignaling', CONFIG.LOG_LEVEL);

export class SfuSignalingClient {
  private ws: WebSocket | null = null;
  private router: RtpRouter;
  private isConnected: boolean = false;
  private reconnectTimer: NodeJS.Timeout | null = null;

  constructor(router: RtpRouter) {
    this.router = router;

    // Listen to PLI / keyframe needed events from router to inform encoders
    this.router.on('keyframe_needed', ({ cameraId, subscriberId }) => {
      this.send({
        type: 'health_report',
        sessionId: CONFIG.SESSION_ID,
        senderId: 'media-server-sfu',
        timestamp: Date.now(),
        sourceId: cameraId,
        sourceType: 'sfu',
        streamMetrics: {
          cameraId,
          bitrate: 0,
          fps: 0,
          packetLoss: 0,
          rtt: 0,
          timestamp: Date.now(),
        },
      });
      logger.info(`Requested keyframe for ${cameraId} on subscriber ${subscriberId} join`);
    });
  }

  public connect(): void {
    if (this.ws) {
      this.ws.removeAllListeners();
      this.ws.close();
    }

    logger.info(`Connecting SFU to Signaling Server at ${CONFIG.SIGNALING_URL}...`);
    this.ws = new WebSocket(CONFIG.SIGNALING_URL);

    this.ws.on('open', () => {
      this.isConnected = true;
      logger.info('✅ SFU connected to Signaling Server');

      // Join session as administrator / SFU media bridge
      this.send({
        type: 'join_session',
        sessionId: CONFIG.SESSION_ID,
        senderId: 'media-server-sfu',
        role: 'admin',
        timestamp: Date.now(),
      });
    });

    this.ws.on('message', (data: WebSocket.Data) => {
      try {
        const msg = JSON.parse(data.toString()) as SignalingMessage;
        this.handleMessage(msg);
      } catch (err) {
        logger.error('Failed to parse signaling message in SFU', err);
      }
    });

    this.ws.on('close', () => {
      this.isConnected = false;
      logger.warn('Signaling connection closed. Retrying in 3s...');
      this.scheduleReconnect();
    });

    this.ws.on('error', (err) => {
      logger.error('WebSocket error in SFU signaling client', err);
    });
  }

  private handleMessage(msg: SignalingMessage): void {
    switch (msg.type) {
      case 'codec_change': {
        const cMsg = msg as CodecChangeMessage;
        logger.info(`Codec change notified: ${cMsg.cameraId} -> ${cMsg.codec}`);
        if (cMsg.cameraId === 'all') {
          for (const p of this.router.getAllProducers()) {
            this.router.setCameraCodec(p.cameraId, cMsg.codec);
          }
        } else {
          this.router.setCameraCodec(cMsg.cameraId, cMsg.codec);
        }
        break;
      }

      case 'presence_update': {
        const pMsg = msg as PresenceUpdateMessage;
        if (pMsg.action === 'left' && pMsg.client.cameraId) {
          logger.info(`Camera ${pMsg.client.cameraId} producer disconnected`);
        }
        break;
      }
    }
  }

  public send(msg: SignalingMessage): void {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = setTimeout(() => {
      this.connect();
    }, 3000);
  }

  public close(): void {
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    if (this.ws) {
      this.ws.removeAllListeners();
      this.ws.close();
    }
  }
}
