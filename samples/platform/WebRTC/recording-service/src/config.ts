import dotenv from 'dotenv';
import path from 'path';
dotenv.config();

export interface RecordingConfig {
  port: number;
  host: string;
  signalingWsUrl: string;
  sfuHttpUrl: string;
  sessionId: string;
  recordingsDir: string;
  segmentDurationSeconds: number;
  s3Endpoint: string;
  s3Bucket: string;
  s3Region: string;
  s3AccessKey: string;
  s3SecretKey: string;
  autoUploadS3: boolean;
  baseRtpPort: number;
}

export const config: RecordingConfig = {
  port: parseInt(process.env.RECORDING_PORT || '3040', 10),
  host: process.env.RECORDING_HOST || '0.0.0.0',
  signalingWsUrl: process.env.SIGNALING_WS_URL || 'ws://localhost:3000/ws',
  sfuHttpUrl: process.env.SFU_HTTP_URL || 'http://localhost:3010',
  sessionId: process.env.SESSION_ID || 'event-001',
  recordingsDir: process.env.RECORDINGS_DIR || path.join(process.cwd(), 'recordings'),
  segmentDurationSeconds: parseInt(process.env.SEGMENT_DURATION_SECONDS || '1800', 10), // 30 minutes default
  s3Endpoint: process.env.S3_ENDPOINT || 'http://localhost:9000',
  s3Bucket: process.env.S3_BUCKET || 'broadcast-recordings',
  s3Region: process.env.S3_REGION || 'us-east-1',
  s3AccessKey: process.env.S3_ACCESS_KEY || 'minioadmin',
  s3SecretKey: process.env.S3_SECRET_KEY || 'minioadmin',
  autoUploadS3: process.env.AUTO_UPLOAD_S3 !== 'false',
  baseRtpPort: parseInt(process.env.RECORDING_BASE_RTP_PORT || '22000', 10),
};
