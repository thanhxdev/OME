import { CodecMode } from '../types/codec';
import { CameraConfig, CameraStatus, TallyState } from '../types/camera';
import { SessionRole, ClientPresence } from '../types/session';
import { StreamMetrics, SystemMetrics } from '../types/metrics';

export type SignalingMessageType =
  | 'join_session'
  | 'joined_session'
  | 'leave_session'
  | 'sdp_offer'
  | 'sdp_answer'
  | 'ice_candidate'
  | 'codec_change'
  | 'codec_changed'
  | 'tally_update'
  | 'intercom_audio'
  | 'health_report'
  | 'recording_command'
  | 'recording_status'
  | 'presence_update'
  | 'session_sync'
  | 'error';

export interface BaseSignalingMessage {
  type: SignalingMessageType;
  sessionId: string;
  senderId: string;
  timestamp: number;
}

export interface JoinSessionMessage extends BaseSignalingMessage {
  type: 'join_session';
  role: SessionRole;
  cameraId?: string;
  machineId?: string;
}

export interface JoinedSessionMessage extends BaseSignalingMessage {
  type: 'joined_session';
  clientId: string;
  role: SessionRole;
  cameras: CameraStatus[];
  presences: ClientPresence[];
}

export interface LeaveSessionMessage extends BaseSignalingMessage {
  type: 'leave_session';
}

export interface SdpOfferMessage extends BaseSignalingMessage {
  type: 'sdp_offer';
  sdp: string;
  cameraId: string;
  codec: CodecMode;
  targetClientId?: string; // If targeting specific subscriber / SFU
}

export interface SdpAnswerMessage extends BaseSignalingMessage {
  type: 'sdp_answer';
  sdp: string;
  cameraId: string;
  targetClientId?: string; // Target encoder / SFU
}

export interface IceCandidateMessage extends BaseSignalingMessage {
  type: 'ice_candidate';
  candidate: RTCIceCandidateInit | Record<string, unknown>;
  cameraId: string;
  targetClientId?: string;
}

export interface CodecChangeMessage extends BaseSignalingMessage {
  type: 'codec_change';
  cameraId: string; // or 'all'
  codec: CodecMode;
  bitrate?: number; // kbps
  resolution?: string;
  fps?: number;
}

export interface CodecChangedNotification extends BaseSignalingMessage {
  type: 'codec_changed';
  cameraId: string;
  codec: CodecMode;
  bitrate?: number;
  resolution?: string;
  fps?: number;
}

export interface TallyUpdateMessage extends BaseSignalingMessage {
  type: 'tally_update';
  cameraId: string;
  state: TallyState;
}

export interface IntercomAudioMessage extends BaseSignalingMessage {
  type: 'intercom_audio';
  from: string;
  to: string; // 'all' or specific clientId / cameraId
  audioData: string; // Base64 encoded opus or PCM payload
}

export interface HealthReportMessage extends BaseSignalingMessage {
  type: 'health_report';
  sourceId: string; // machineId or cameraId
  sourceType: 'encoder' | 'decoder' | 'sfu' | 'agent';
  streamMetrics?: StreamMetrics;
  systemMetrics?: SystemMetrics;
}

export interface RecordingCommandMessage extends BaseSignalingMessage {
  type: 'recording_command';
  cameraId: string; // or 'all'
  action: 'start' | 'stop';
}

export interface RecordingStatusMessage extends BaseSignalingMessage {
  type: 'recording_status';
  cameraId: string;
  isRecording: boolean;
  durationSeconds?: number;
  fileSizeBytes?: number;
  currentFilename?: string;
}

export interface PresenceUpdateMessage extends BaseSignalingMessage {
  type: 'presence_update';
  action: 'joined' | 'left' | 'status_change';
  client: ClientPresence;
}

export interface SessionSyncMessage extends BaseSignalingMessage {
  type: 'session_sync';
  sessionName: string;
  cameras: CameraStatus[];
}

export interface ErrorMessage extends BaseSignalingMessage {
  type: 'error';
  code: string;
  message: string;
  details?: unknown;
}

export type SignalingMessage =
  | JoinSessionMessage
  | JoinedSessionMessage
  | LeaveSessionMessage
  | SdpOfferMessage
  | SdpAnswerMessage
  | IceCandidateMessage
  | CodecChangeMessage
  | CodecChangedNotification
  | TallyUpdateMessage
  | IntercomAudioMessage
  | HealthReportMessage
  | RecordingCommandMessage
  | RecordingStatusMessage
  | PresenceUpdateMessage
  | SessionSyncMessage
  | ErrorMessage;
