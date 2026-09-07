import { CameraConfig } from './camera';

export type SessionRole = 'encoder' | 'decoder' | 'monitor' | 'admin';

export type SessionStatus = 'created' | 'active' | 'ended';

export interface Session {
  id: string; // e.g. "event-001"
  name: string; // e.g. "Championship Match Final"
  status: SessionStatus;
  cameras: CameraConfig[];
  startedAt?: number;
  endedAt?: number;
  description?: string;
  metadata?: Record<string, unknown>;
}

export interface ClientPresence {
  clientId: string;
  role: SessionRole;
  sessionId: string;
  cameraId?: string;
  machineId?: string;
  connectedAt: number;
}
