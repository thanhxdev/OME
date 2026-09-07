import {
  Session,
  SessionStatus,
  CameraConfig,
  CameraStatus,
  ClientPresence,
  TallyState,
  CodecMode,
  StreamMetrics,
  Logger,
} from '@webrtc-broadcast/shared';
import { CONFIG } from '../config';

const logger = new Logger('SessionManager', CONFIG.LOG_LEVEL);

export class SessionManager {
  private sessions: Map<string, Session> = new Map();
  private cameraStatuses: Map<string, Map<string, CameraStatus>> = new Map(); // sessionId -> (cameraId -> status)
  private presences: Map<string, Map<string, ClientPresence>> = new Map(); // sessionId -> (clientId -> presence)

  constructor() {
    // Initialize default event session for immediate zero-config plug & play
    this.createSession({
      id: 'event-001',
      name: 'Default Stadium Live Broadcast',
      status: 'active',
      cameras: [
        { id: 'cam-01', name: 'Main Wide (SDI 1)', inputType: 'sdi', deviceIndex: 0, codec: 'h264', resolution: '1920x1080', fps: 60, bitrate: 10000 },
        { id: 'cam-02', name: 'Tight Close-up (SDI 2)', inputType: 'sdi', deviceIndex: 1, codec: 'h264', resolution: '1920x1080', fps: 60, bitrate: 10000 },
        { id: 'cam-03', name: 'Goal Left (HDMI 1)', inputType: 'hdmi', deviceIndex: 0, codec: 'h264', resolution: '1920x1080', fps: 60, bitrate: 10000 },
        { id: 'cam-04', name: 'Goal Right (HDMI 2)', inputType: 'hdmi', deviceIndex: 1, codec: 'h264', resolution: '1920x1080', fps: 60, bitrate: 10000 },
        { id: 'cam-05', name: 'Bench Home (SDI 3)', inputType: 'sdi', deviceIndex: 2, codec: 'h264', resolution: '1920x1080', fps: 60, bitrate: 8000 },
        { id: 'cam-06', name: 'Bench Away (SDI 4)', inputType: 'sdi', deviceIndex: 3, codec: 'h264', resolution: '1920x1080', fps: 60, bitrate: 8000 },
        { id: 'cam-07', name: 'Spidercam High (SDI 5)', inputType: 'sdi', deviceIndex: 4, codec: 'throughpass', resolution: '1920x1080', fps: 60, bitrate: 14000 },
        { id: 'cam-08', name: 'Close-up Center (HDMI 3)', inputType: 'hdmi', deviceIndex: 2, codec: 'h265', resolution: '1920x1080', fps: 60, bitrate: 6000 },
        { id: 'cam-09', name: 'Tunnel Entry (HDMI 4)', inputType: 'hdmi', deviceIndex: 3, codec: 'h264', resolution: '1920x1080', fps: 60, bitrate: 8000 },
        { id: 'cam-10', name: 'Beauty Exterior (SDI 6)', inputType: 'sdi', deviceIndex: 5, codec: 'h264', resolution: '1920x1080', fps: 60, bitrate: 8000 },
      ],
    });
  }

  public createSession(data: { id: string; name: string; status?: SessionStatus; cameras?: CameraConfig[] }): Session {
    if (this.sessions.has(data.id)) {
      throw new Error(`Session with ID ${data.id} already exists`);
    }

    const session: Session = {
      id: data.id,
      name: data.name,
      status: data.status || 'created',
      cameras: (data.cameras || []).slice(0, CONFIG.MAX_CAMERAS),
      startedAt: data.status === 'active' ? Date.now() : undefined,
    };

    this.sessions.set(session.id, session);
    this.cameraStatuses.set(session.id, new Map());
    this.presences.set(session.id, new Map());

    // Initialize statuses for predefined cameras
    for (const cam of session.cameras) {
      this.updateCameraStatus(session.id, cam.id, {
        id: cam.id,
        name: cam.name,
        online: false,
        codec: cam.codec,
        currentBitrate: cam.bitrate || 10000,
        packetLoss: 0,
        rtt: 0,
        fps: cam.fps,
        tally: 'off',
      });
    }

    logger.info(`Session created: ${session.id} (${session.name})`);
    return session;
  }

  public getSession(id: string): Session | undefined {
    return this.sessions.get(id);
  }

  public listSessions(): Session[] {
    return Array.from(this.sessions.values());
  }

  public updateSession(id: string, updates: Partial<Session>): Session {
    const session = this.sessions.get(id);
    if (!session) {
      throw new Error(`Session ${id} not found`);
    }

    if (updates.name) session.name = updates.name;
    if (updates.status) {
      session.status = updates.status;
      if (updates.status === 'active' && !session.startedAt) {
        session.startedAt = Date.now();
      } else if (updates.status === 'ended' && !session.endedAt) {
        session.endedAt = Date.now();
      }
    }
    if (updates.cameras) {
      session.cameras = updates.cameras.slice(0, CONFIG.MAX_CAMERAS);
    }

    return session;
  }

  public deleteSession(id: string): boolean {
    this.cameraStatuses.delete(id);
    this.presences.delete(id);
    return this.sessions.delete(id);
  }

  public addCameraToSession(sessionId: string, camera: CameraConfig): CameraConfig {
    const session = this.sessions.get(sessionId);
    if (!session) throw new Error(`Session ${sessionId} not found`);

    if (session.cameras.length >= CONFIG.MAX_CAMERAS) {
      throw new Error(`Maximum camera capacity reached (${CONFIG.MAX_CAMERAS} cameras max)`);
    }

    const existing = session.cameras.find((c) => c.id === camera.id);
    if (existing) {
      throw new Error(`Camera with ID ${camera.id} already exists in this session`);
    }

    session.cameras.push(camera);
    this.updateCameraStatus(sessionId, camera.id, {
      id: camera.id,
      name: camera.name,
      online: false,
      codec: camera.codec,
      currentBitrate: camera.bitrate || 10000,
      packetLoss: 0,
      rtt: 0,
      fps: camera.fps,
      tally: 'off',
    });

    return camera;
  }

  public updateCameraStatus(sessionId: string, cameraId: string, updates: Partial<CameraStatus>): CameraStatus {
    let sessionCams = this.cameraStatuses.get(sessionId);
    if (!sessionCams) {
      sessionCams = new Map();
      this.cameraStatuses.set(sessionId, sessionCams);
    }

    const current = sessionCams.get(cameraId) || {
      id: cameraId,
      online: false,
      codec: 'h264',
      currentBitrate: 0,
      packetLoss: 0,
      rtt: 0,
      fps: 0,
      tally: 'off',
    };

    const updated: CameraStatus = {
      ...current,
      ...updates,
      lastSeen: Date.now(),
    };

    sessionCams.set(cameraId, updated);
    return updated;
  }

  public updateCameraTally(sessionId: string, cameraId: string, state: TallyState): CameraStatus | undefined {
    return this.updateCameraStatus(sessionId, cameraId, { tally: state });
  }

  public updateCameraCodec(sessionId: string, cameraId: string, codec: CodecMode, bitrate?: number): CameraStatus | undefined {
    return this.updateCameraStatus(sessionId, cameraId, {
      codec,
      ...(bitrate ? { currentBitrate: bitrate } : {}),
    });
  }

  public recordStreamMetrics(sessionId: string, metrics: StreamMetrics): void {
    this.updateCameraStatus(sessionId, metrics.cameraId, {
      online: true,
      currentBitrate: metrics.bitrate,
      fps: metrics.fps,
      packetLoss: metrics.packetLoss,
      rtt: metrics.rtt,
    });
  }

  public getCameraStatuses(sessionId: string): CameraStatus[] {
    const sessionCams = this.cameraStatuses.get(sessionId);
    if (!sessionCams) return [];
    return Array.from(sessionCams.values());
  }

  public addPresence(presence: ClientPresence): void {
    let sessionPresences = this.presences.get(presence.sessionId);
    if (!sessionPresences) {
      sessionPresences = new Map();
      this.presences.set(presence.sessionId, sessionPresences);
    }
    sessionPresences.set(presence.clientId, presence);

    if (presence.role === 'encoder' && presence.cameraId) {
      this.updateCameraStatus(presence.sessionId, presence.cameraId, { online: true });
    }
  }

  public removePresence(sessionId: string, clientId: string): ClientPresence | undefined {
    const sessionPresences = this.presences.get(sessionId);
    if (!sessionPresences) return undefined;

    const presence = sessionPresences.get(clientId);
    if (presence) {
      sessionPresences.delete(clientId);
      if (presence.role === 'encoder' && presence.cameraId) {
        this.updateCameraStatus(sessionId, presence.cameraId, { online: false });
      }
    }
    return presence;
  }

  public getPresences(sessionId: string): ClientPresence[] {
    const sessionPresences = this.presences.get(sessionId);
    if (!sessionPresences) return [];
    return Array.from(sessionPresences.values());
  }
}
