import dotenv from 'dotenv';
import { DEFAULT_PORTS } from '@webrtc-broadcast/shared';

dotenv.config();

export const CONFIG = {
  PORT: parseInt(process.env.PORT || `${DEFAULT_PORTS.MEDIA_SERVER_HTTP}`, 10),
  SIGNALING_URL: process.env.SIGNALING_URL || `ws://localhost:${DEFAULT_PORTS.SIGNALING_HTTP}/ws`,
  SESSION_ID: process.env.SESSION_ID || 'event-001',
  RTC_MIN_PORT: parseInt(process.env.MIN_PORT || `${DEFAULT_PORTS.MEDIA_SERVER_RTC_MIN}`, 10),
  RTC_MAX_PORT: parseInt(process.env.MAX_PORT || `${DEFAULT_PORTS.MEDIA_SERVER_RTC_MAX}`, 10),
  ANNOUNCED_IP: process.env.ANNOUNCED_IP || '127.0.0.1',
  CORS_ORIGIN: process.env.CORS_ORIGIN || '*',
  LOG_LEVEL: (process.env.LOG_LEVEL || 'info') as 'debug' | 'info' | 'warn' | 'error',
};
