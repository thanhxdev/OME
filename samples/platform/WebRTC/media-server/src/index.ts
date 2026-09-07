import http from 'http';
import express from 'express';
import cors from 'cors';
import { Logger } from '@webrtc-broadcast/shared';
import { CONFIG } from './config';
import { RtpRouter } from './sfu/rtp-router';
import { SfuSignalingClient } from './signaling-client';
import { createMediaApiRouter } from './api/server';

const logger = new Logger('MediaServerApp', CONFIG.LOG_LEVEL);

async function bootstrap() {
  const app = express();
  app.use(cors({ origin: CONFIG.CORS_ORIGIN }));
  app.use(express.json());

  // Initialize RTP Router (SFU)
  const router = new RtpRouter();

  // Pre-register ports for 10 broadcast cameras
  for (let i = 1; i <= 10; i++) {
    const camId = `cam-${i.toString().padStart(2, '0')}`;
    router.registerCamera(camId, 'h264');
  }

  // Connect to Signaling Server
  const signalingClient = new SfuSignalingClient(router);
  signalingClient.connect();

  // Mount Media Server API
  app.use('/api', createMediaApiRouter(router));

  app.get('/', (_req, res) => {
    res.json({
      name: 'Broadcast WebRTC Media Server (SFU)',
      version: '1.0.0',
      streamsEndpoint: '/api/streams',
      subscribersEndpoint: '/api/subscribers',
      health: '/api/health',
    });
  });

  const server = http.createServer(app);

  server.listen(CONFIG.PORT, () => {
    logger.info(`====================================================`);
    logger.info(`🚀 SFU Media Server listening on http://localhost:${CONFIG.PORT}`);
    logger.info(`📡 Ingress RTP ports range: UDP ${CONFIG.RTC_MIN_PORT} - ${CONFIG.RTC_MAX_PORT}`);
    logger.info(`🔄 Signaling relay targeted at: ${CONFIG.SIGNALING_URL}`);
    logger.info(`====================================================`);
  });

  // Graceful termination
  const shutdown = () => {
    logger.info('Shutting down SFU Media Server...');
    signalingClient.close();
    router.close();
    server.close(() => {
      logger.info('SFU Media Server gracefully stopped.');
      process.exit(0);
    });
  };

  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

bootstrap().catch((err) => {
  logger.error('Fatal error starting Media Server SFU', err);
  process.exit(1);
});
