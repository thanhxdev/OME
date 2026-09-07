import dotenv from 'dotenv';
import { DEFAULT_PORTS } from '@webrtc-broadcast/shared';

dotenv.config();

export const CONFIG = {
  PORT: parseInt(process.env.PORT || `${DEFAULT_PORTS.SIGNALING_HTTP}`, 10),
  JWT_SECRET: process.env.JWT_SECRET || 'broadcast_jwt_secret_dev_key_change_in_prod',
  JWT_EXPIRES_IN: process.env.JWT_EXPIRES_IN || '24h',
  MAX_CAMERAS: 10,
  CORS_ORIGIN: process.env.CORS_ORIGIN || '*',
  LOG_LEVEL: (process.env.LOG_LEVEL || 'info') as 'debug' | 'info' | 'warn' | 'error',
};
