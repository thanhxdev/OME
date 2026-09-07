import http from 'http';
import express from 'express';
import cors from 'cors';
import { Logger } from '@webrtc-broadcast/shared';
import { CONFIG } from './config';
import { SessionManager } from './session/session-manager';
import { SignalingWsServer } from './signaling/ws-server';
import { createApiRouter } from './api/routes';

const logger = new Logger('SignalingApp', CONFIG.LOG_LEVEL);

async function bootstrap() {
  const app = express();
  app.use(cors({ origin: CONFIG.CORS_ORIGIN }));
  app.use(express.json());

  // Initialize Session Manager
  const sessionManager = new SessionManager();

  // Register REST API Routes
  app.use('/api', createApiRouter(sessionManager));

  // Root endpoint info
  app.get('/', (_req, res) => {
    res.json({
      name: 'Broadcast WebRTC Signaling Server',
      version: '1.0.0',
      websocket: `ws://localhost:${CONFIG.PORT}/ws`,
      apiDocs: '/api/sessions',
      health: '/api/health',
    });
  });

  // Create HTTP and WebSocket server
  const server = http.createServer(app);
  const wsServer = new SignalingWsServer(server, sessionManager);

  server.listen(CONFIG.PORT, () => {
    logger.info(`====================================================`);
    logger.info(`🚀 Signaling Server running on http://localhost:${CONFIG.PORT}`);
    logger.info(`📡 WebSocket Signaling listener at ws://localhost:${CONFIG.PORT}/ws`);
    logger.info(`====================================================`);
  });

  // Handle graceful shutdown
  const shutdown = () => {
    logger.info('Shutting down Signaling Server...');
    server.close(() => {
      logger.info('HTTP & WebSocket servers closed.');
      process.exit(0);
    });
  };

  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

bootstrap().catch((err) => {
  logger.error('Failed to start Signaling Server', err);
  process.exit(1);
});
