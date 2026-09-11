import { WebSocket, WebSocketServer } from 'ws';
import { IncomingMessage } from 'http';
import {
  SignalingMessage,
  JoinSessionMessage,
  SdpOfferMessage,
  SdpAnswerMessage,
  IceCandidateMessage,
  CodecChangeMessage,
  TallyUpdateMessage,
  IntercomAudioMessage,
  HealthReportMessage,
  RecordingCommandMessage,
  RecordingStatusMessage,
  PortAllocateRequestMessage,
  PortAllocateResponseMessage,
  StreamPublishedMessage,
  Logger,
  SessionRole,
} from '@webrtc-broadcast/shared';
import { SessionManager } from '../session/session-manager';
import { filterSdpByCodec } from './sdp-filter';
import { CONFIG } from '../config';

const logger = new Logger('SignalingWs', CONFIG.LOG_LEVEL);

interface AuthenticatedSocket extends WebSocket {
  clientId: string;
  sessionId?: string;
  role?: SessionRole;
  cameraId?: string;
  isAlive: boolean;
}

export class SignalingWsServer {
  private wss: WebSocketServer;
  private sessionManager: SessionManager;
  private clients: Map<string, AuthenticatedSocket> = new Map(); // clientId -> socket
  private rooms: Map<string, Set<string>> = new Map(); // sessionId -> Set of clientIds

  constructor(server: any, sessionManager: SessionManager) {
    this.sessionManager = sessionManager;
    this.wss = new WebSocketServer({ server, path: '/ws' });

    this.wss.on('connection', (ws: WebSocket, req: IncomingMessage) => {
      this.handleConnection(ws as AuthenticatedSocket, req);
    });

    // Heartbeat check interval
    setInterval(() => {
      this.clients.forEach((ws) => {
        if (!ws.isAlive) {
          logger.warn(`Terminating inactive socket ${ws.clientId}`);
          return ws.terminate();
        }
        ws.isAlive = false;
        ws.ping();
      });
    }, 30000);

    logger.info('WebSocket Signaling Server attached to /ws');
  }

  private handleConnection(ws: AuthenticatedSocket, req: IncomingMessage): void {
    const clientId = `client_${Date.now()}_${Math.random().toString(36).substring(2, 7)}`;
    ws.clientId = clientId;
    ws.isAlive = true;

    this.clients.set(clientId, ws);
    logger.info(`Client connected: ${clientId} from ${req.socket.remoteAddress}`);

    ws.on('pong', () => {
      ws.isAlive = true;
    });

    ws.on('message', (raw: Buffer) => {
      try {
        const msg = JSON.parse(raw.toString()) as SignalingMessage;
        this.processMessage(ws, msg);
      } catch (err) {
        logger.error(`Error parsing message from ${ws.clientId}`, err);
        this.sendToSocket(ws, {
          type: 'error',
          sessionId: ws.sessionId || 'unknown',
          senderId: 'signaling-server',
          timestamp: Date.now(),
          code: 'INVALID_JSON',
          message: 'Malformed JSON payload',
        });
      }
    });

    ws.on('close', () => {
      this.handleDisconnect(ws);
    });

    ws.on('error', (err) => {
      logger.error(`Socket error on ${ws.clientId}`, err);
    });
  }

  private processMessage(ws: AuthenticatedSocket, msg: SignalingMessage): void {
    switch ((msg as any).type) {
      case 'join_session':
        this.handleJoinSession(ws, msg as JoinSessionMessage);
        break;

      case 'leave_session':
        this.handleLeaveSession(ws);
        break;

      case 'sdp_offer':
        this.handleSdpOffer(ws, msg as SdpOfferMessage);
        break;

      case 'sdp_answer':
        this.handleSdpAnswer(ws, msg as SdpAnswerMessage);
        break;

      case 'ice_candidate':
        this.handleIceCandidate(ws, msg as IceCandidateMessage);
        break;

      case 'codec_change':
        this.handleCodecChange(ws, msg as CodecChangeMessage);
        break;

      case 'tally_update':
        this.handleTallyUpdate(ws, msg as TallyUpdateMessage);
        break;

      case 'intercom_audio':
        this.handleIntercomAudio(ws, msg as IntercomAudioMessage);
        break;

      case 'intercom_state':
        this.handleIntercomState(ws, msg as any);
        break;

      case 'health_report':
        this.handleHealthReport(ws, msg as HealthReportMessage);
        break;

      case 'recording_command':
        this.handleRecordingCommand(ws, msg as RecordingCommandMessage);
        break;

      case 'recording_status':
        this.handleRecordingStatus(ws, msg as RecordingStatusMessage);
        break;

      case 'port_allocate_request':
        this.handlePortAllocateRequest(ws, msg as PortAllocateRequestMessage);
        break;

      case 'stream_published':
        this.handleStreamPublished(ws, msg as StreamPublishedMessage);
        break;

      case 'camera_meta_update':
        this.handleCameraMetaUpdate(ws, msg);
        break;

      case 'decoder_ready':
        this.handleDecoderReady(ws, msg);
        break;

      default:
        logger.warn(`Unknown message type received: ${(msg as any).type}`);
    }
  }

  private handleJoinSession(ws: AuthenticatedSocket, msg: JoinSessionMessage): void {
    const sessionId = msg.sessionId || 'broadcast-01';
    let session = this.sessionManager.getSession(sessionId);

    if (!session) {
      try {
        session = this.sessionManager.createSession({
          id: sessionId,
          name: `Live Session ${sessionId}`,
          status: 'active',
          cameras: Array.from({ length: 10 }, (_, i) => ({
            id: `cam-${(i + 1).toString().padStart(2, '0')}`,
            name: `CAM ${i + 1}`,
            inputType: (i % 2 === 0 ? 'sdi' : 'hdmi') as 'sdi' | 'hdmi',
            deviceIndex: i,
            codec: 'h264',
            resolution: '1920x1080',
            fps: 60,
            bitrate: 10000,
          })),
        });
      } catch {
        session = this.sessionManager.getSession(sessionId);
      }
    }

    ws.sessionId = sessionId;
    ws.role = msg.role;
    ws.cameraId = msg.cameraId;

    let room = this.rooms.get(sessionId);
    if (!room) {
      room = new Set();
      this.rooms.set(sessionId, room);
    }
    room.add(ws.clientId);

    // Register presence in session manager
    this.sessionManager.addPresence({
      clientId: ws.clientId,
      role: msg.role,
      sessionId,
      cameraId: msg.cameraId,
      machineId: msg.machineId,
      connectedAt: Date.now(),
    });

    logger.info(`Client ${ws.clientId} joined session ${sessionId} as ${msg.role} (cam: ${msg.cameraId || 'none'})`);

    // Reply joined_session acknowledgment with current states
    this.sendToSocket(ws, {
      type: 'joined_session',
      sessionId,
      senderId: 'signaling-server',
      timestamp: Date.now(),
      clientId: ws.clientId,
      role: msg.role,
      cameras: this.sessionManager.getCameraStatuses(sessionId),
      presences: this.sessionManager.getPresences(sessionId),
    });

    // Notify room of presence update
    this.broadcastToRoom(
      sessionId,
      {
        type: 'presence_update',
        sessionId,
        senderId: ws.clientId,
        timestamp: Date.now(),
        action: 'joined',
        client: {
          clientId: ws.clientId,
          role: msg.role,
          sessionId,
          cameraId: msg.cameraId,
          machineId: msg.machineId,
          connectedAt: Date.now(),
        },
      },
      ws.clientId
    );
  }

  private handleLeaveSession(ws: AuthenticatedSocket): void {
    if (!ws.sessionId) return;
    const sessionId = ws.sessionId;

    const room = this.rooms.get(sessionId);
    if (room) {
      room.delete(ws.clientId);
    }

    const presence = this.sessionManager.removePresence(sessionId, ws.clientId);
    if (presence) {
      this.broadcastToRoom(sessionId, {
        type: 'presence_update',
        sessionId,
        senderId: ws.clientId,
        timestamp: Date.now(),
        action: 'left',
        client: presence,
      });
    }

    ws.sessionId = undefined;
    ws.role = undefined;
  }

  private handleSdpOffer(ws: AuthenticatedSocket, msg: SdpOfferMessage): void {
    if (!ws.sessionId) return;

    // Apply Codec negotiation & filtering
    const filteredSdp = filterSdpByCodec(msg.sdp, msg.codec);
    const forwardedMsg: SdpOfferMessage = {
      ...msg,
      sdp: filteredSdp,
      senderId: ws.clientId,
    };

    if (msg.targetClientId) {
      // Direct relay to specific subscriber / SFU
      this.sendToClient(msg.targetClientId, forwardedMsg);
    } else {
      // Broadcast to decoders / SFU in room
      this.broadcastToRole(ws.sessionId, ['decoder', 'admin'], forwardedMsg, ws.clientId);
    }
  }

  private handleSdpAnswer(ws: AuthenticatedSocket, msg: SdpAnswerMessage): void {
    if (!ws.sessionId) return;

    const forwardedMsg: SdpAnswerMessage = {
      ...msg,
      senderId: ws.clientId,
    };

    if (msg.targetClientId) {
      this.sendToClient(msg.targetClientId, forwardedMsg);
    } else {
      this.broadcastToRole(ws.sessionId, ['encoder'], forwardedMsg, ws.clientId);
    }
  }

  private handleIceCandidate(ws: AuthenticatedSocket, msg: IceCandidateMessage): void {
    if (!ws.sessionId) return;

    const forwardedMsg: IceCandidateMessage = {
      ...msg,
      senderId: ws.clientId,
    };

    if (msg.targetClientId) {
      this.sendToClient(msg.targetClientId, forwardedMsg);
    } else {
      this.broadcastToRoom(ws.sessionId, forwardedMsg, ws.clientId);
    }
  }

  private handleCodecChange(ws: AuthenticatedSocket, msg: CodecChangeMessage): void {
    if (!ws.sessionId) return;

    logger.info(`CodecChange requested for cam ${msg.cameraId} -> ${msg.codec} (${msg.bitrate || 'auto'} kbps)`);

    if (msg.cameraId !== 'all') {
      this.sessionManager.updateCameraCodec(ws.sessionId, msg.cameraId, msg.codec, msg.bitrate);
    }

    // Forward to encoder agents & encoders
    this.broadcastToRole(ws.sessionId, ['encoder', 'admin', 'monitor'], {
      ...msg,
      senderId: ws.clientId,
    });
  }

  private handleTallyUpdate(ws: AuthenticatedSocket, msg: TallyUpdateMessage): void {
    if (!ws.sessionId) return;

    this.sessionManager.updateCameraTally(ws.sessionId, msg.cameraId, msg.state);

    // Broadcast tally to encoders, monitor-wall, control-dashboard
    this.broadcastToRoom(ws.sessionId, {
      ...msg,
      senderId: ws.clientId,
    });
  }

  private handleIntercomAudio(ws: AuthenticatedSocket, msg: IntercomAudioMessage): void {
    if (!ws.sessionId) return;

    if (msg.to === 'all') {
      this.broadcastToRoom(ws.sessionId, msg, ws.clientId);
    } else {
      let sent = false;
      const room = this.rooms.get(ws.sessionId);
      if (room) {
        const payload = JSON.stringify(msg);
        room.forEach((cid) => {
          const clientWs = this.clients.get(cid);
          if (clientWs && (clientWs.cameraId === msg.to || clientWs.clientId === msg.to) && clientWs.readyState === WebSocket.OPEN) {
            clientWs.send(payload);
            sent = true;
          }
        });
      }
      if (!sent) {
        this.sendToClient(msg.to, msg);
      }
    }
  }

  private handleIntercomState(ws: AuthenticatedSocket, msg: any): void {
    if (!ws.sessionId) return;

    if (msg.to === 'all') {
      this.broadcastToRoom(ws.sessionId, msg, ws.clientId);
    } else {
      let sent = false;
      const room = this.rooms.get(ws.sessionId);
      if (room) {
        const payload = JSON.stringify(msg);
        room.forEach((cid) => {
          const clientWs = this.clients.get(cid);
          if (clientWs && (clientWs.cameraId === msg.to || clientWs.clientId === msg.to) && clientWs.readyState === WebSocket.OPEN) {
            clientWs.send(payload);
            sent = true;
          }
        });
      }
      if (!sent) {
        this.sendToClient(msg.to, msg);
      }
    }
  }

  private handleHealthReport(ws: AuthenticatedSocket, msg: HealthReportMessage): void {
    if (!ws.sessionId) return;

    if (msg.streamMetrics) {
      this.sessionManager.recordStreamMetrics(ws.sessionId, msg.streamMetrics);
    }

    // Broadcast telemetry to monitor-wall, dashboard, and monitoring service
    this.broadcastToRole(ws.sessionId, ['admin', 'monitor'], {
      ...msg,
      senderId: ws.clientId,
    });
  }

  private handleRecordingCommand(ws: AuthenticatedSocket, msg: RecordingCommandMessage): void {
    if (!ws.sessionId) return;
    logger.info(`Recording command received: ${msg.action} for camera ${msg.cameraId}`);
    // Forward command to recording service (role 'decoder' or 'admin')
    this.broadcastToRoom(ws.sessionId, {
      ...msg,
      senderId: ws.clientId,
    });
  }

  private handleRecordingStatus(ws: AuthenticatedSocket, msg: RecordingStatusMessage): void {
    if (!ws.sessionId) return;
    this.sessionManager.updateCameraStatus(ws.sessionId, msg.cameraId, {
      isRecording: msg.isRecording,
    });
    this.broadcastToRole(ws.sessionId, ['admin', 'monitor'], msg);
  }

  private async handlePortAllocateRequest(ws: AuthenticatedSocket, msg: PortAllocateRequestMessage): Promise<void> {
    const sessionId = ws.sessionId || msg.sessionId;
    const cameraId = msg.cameraId;
    const isSinglePort = msg.isSinglePort === true;
    logger.info(`Port allocate request from ${ws.clientId} for camera ${cameraId} (SinglePort: ${isSinglePort})`);

    let videoPort: number;
    let audioPort: number;
    const sfuHost = process.env.SFU_HOST || '127.0.0.1';
    const mediaServerApi = process.env.MEDIA_SERVER_API_URL || 'http://127.0.0.1:4000';

    try {
      // 1. Request dynamic port allocation from SFU Media Server API
      const res = await fetch(`${mediaServerApi}/api/streams/${cameraId}/allocate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isSinglePort }),
      });

      if (res.ok) {
        const data = (await res.json()) as { videoPort: number; audioPort: number; isSinglePort: boolean };
        videoPort = data.videoPort;
        audioPort = data.audioPort;
        logger.info(`SFU allocated ports for ${cameraId}: Video UDP ${videoPort}, Audio UDP ${audioPort}`);
      } else {
        throw new Error(`SFU returned HTTP ${res.status}`);
      }
    } catch (err: any) {
      logger.warn(`SFU allocate endpoint unavailable (${err?.message || err}), using fallback dynamic port allocation`);
      const match = cameraId.match(/cam-(\d+)/);
      const camIdx = match ? parseInt(match[1], 10) - 1 : 0;
      videoPort = 10000 + (camIdx * 4);
      audioPort = isSinglePort ? videoPort : videoPort + 2;
    }

    const response: PortAllocateResponseMessage = {
      type: 'port_allocate_response',
      sessionId,
      senderId: 'signaling-server',
      timestamp: Date.now(),
      cameraId,
      isSinglePort,
      videoPort,
      audioPort,
      sfuHost,
    };

    this.sendToSocket(ws, response);
  }

  private handleStreamPublished(ws: AuthenticatedSocket, msg: StreamPublishedMessage): void {
    const sessionId = ws.sessionId || msg.sessionId;
    logger.info(`Camera stream published: ${msg.cameraId} (Video UDP: ${msg.videoPort}, Audio UDP: ${msg.audioPort}, ${msg.isSinglePort ? 'BUNDLE' : 'SPLIT'}, Codec: ${msg.codec})`);

    if (sessionId) {
      this.sessionManager.updateCameraStatus(sessionId, msg.cameraId, {
        online: true,
        codec: msg.codec as any,
        lastSeen: Date.now(),
        ...((msg as any).cameraName ? { name: (msg as any).cameraName } : {}),
      } as any);

      // Broadcast stream_published to all clients in session
      this.broadcastToRoom(sessionId, {
        ...msg,
        senderId: ws.clientId,
      });
    }
  }

  private handleCameraMetaUpdate(ws: AuthenticatedSocket, msg: any): void {
    const sessionId = ws.sessionId || msg.sessionId;
    logger.info(`Camera meta update: ${msg.cameraId} -> "${msg.cameraName}"`);
    if (sessionId) {
      if (msg.cameraName) {
        this.sessionManager.updateCameraStatus(sessionId, msg.cameraId, {
          name: msg.cameraName,
        } as any);
      }
      this.broadcastToRoom(sessionId, {
        ...msg,
        senderId: ws.clientId,
      });
    }
  }

  private handleDecoderReady(ws: AuthenticatedSocket, msg: any): void {
    const sessionId = ws.sessionId || msg.sessionId;
    logger.info(`Decoder ready for ${msg.cameraId} -> Video UDP ${msg.videoPort}, Audio UDP ${msg.audioPort}`);
    if (sessionId) {
      this.broadcastToRoom(sessionId, {
        ...msg,
        senderId: ws.clientId,
      });
    }
  }

  private handleDisconnect(ws: AuthenticatedSocket): void {
    logger.info(`Client disconnected: ${ws.clientId}`);
    this.handleLeaveSession(ws);
    this.clients.delete(ws.clientId);
  }

  private sendToSocket(ws: WebSocket, msg: SignalingMessage): void {
    if (ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(msg));
    }
  }

  public sendToClient(clientId: string, msg: SignalingMessage): boolean {
    const socket = this.clients.get(clientId);
    if (socket && socket.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify(msg));
      return true;
    }
    return false;
  }

  public broadcastToRoom(sessionId: string, msg: SignalingMessage, excludeClientId?: string): void {
    const room = this.rooms.get(sessionId);
    if (!room) return;

    const payload = JSON.stringify(msg);
    room.forEach((cid) => {
      if (cid !== excludeClientId) {
        const socket = this.clients.get(cid);
        if (socket && socket.readyState === WebSocket.OPEN) {
          socket.send(payload);
        }
      }
    });
  }

  public broadcastToRole(sessionId: string, roles: SessionRole[], msg: SignalingMessage, excludeClientId?: string): void {
    const room = this.rooms.get(sessionId);
    if (!room) return;

    const payload = JSON.stringify(msg);
    room.forEach((cid) => {
      if (cid !== excludeClientId) {
        const socket = this.clients.get(cid);
        if (socket && socket.readyState === WebSocket.OPEN && socket.role && roles.includes(socket.role)) {
          socket.send(payload);
        }
      }
    });
  }
}
