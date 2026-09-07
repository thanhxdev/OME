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
    switch (msg.type) {
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

      case 'health_report':
        this.handleHealthReport(ws, msg as HealthReportMessage);
        break;

      case 'recording_command':
        this.handleRecordingCommand(ws, msg as RecordingCommandMessage);
        break;

      case 'recording_status':
        this.handleRecordingStatus(ws, msg as RecordingStatusMessage);
        break;

      default:
        logger.warn(`Unknown message type received: ${(msg as any).type}`);
    }
  }

  private handleJoinSession(ws: AuthenticatedSocket, msg: JoinSessionMessage): void {
    const sessionId = msg.sessionId;
    const session = this.sessionManager.getSession(sessionId);

    if (!session) {
      this.sendToSocket(ws, {
        type: 'error',
        sessionId,
        senderId: 'signaling-server',
        timestamp: Date.now(),
        code: 'SESSION_NOT_FOUND',
        message: `Session ${sessionId} does not exist`,
      });
      return;
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
      this.sendToClient(msg.to, msg);
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
