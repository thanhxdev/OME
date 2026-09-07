import dotenv from 'dotenv';
dotenv.config();

export interface MonitoringConfig {
  port: number;
  host: string;
  signalingWsUrl: string;
  sfuHttpUrl: string;
  sessionId: string;
  scrapeIntervalMs: number;
  wanBandwidthLimitKbps: number;
  wanBandwidthWarningKbps: number;
}

export const config: MonitoringConfig = {
  port: parseInt(process.env.MONITORING_PORT || '9100', 10),
  host: process.env.MONITORING_HOST || '0.0.0.0',
  signalingWsUrl: process.env.SIGNALING_WS_URL || 'ws://localhost:3000/ws',
  sfuHttpUrl: process.env.SFU_HTTP_URL || 'http://localhost:3010',
  sessionId: process.env.SESSION_ID || 'event-001',
  scrapeIntervalMs: parseInt(process.env.SCRAPE_INTERVAL_MS || '2000', 10),
  wanBandwidthLimitKbps: parseInt(process.env.WAN_BANDWIDTH_LIMIT_KBPS || '120000', 10), // 120 Mbps
  wanBandwidthWarningKbps: parseInt(process.env.WAN_BANDWIDTH_WARNING_KBPS || '95000', 10), // 95 Mbps
};
