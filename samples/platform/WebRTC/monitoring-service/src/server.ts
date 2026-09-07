import express, { Request, Response } from 'express';
import cors from 'cors';
import { metricsRegistry } from './metrics-registry';
import { TelemetryCollector } from './collector';

export function createMonitoringServer(collector: TelemetryCollector) {
  const app = express();

  app.use(cors());
  app.use(express.json());

  // Prometheus scrape endpoint
  app.get('/metrics', async (_req: Request, res: Response) => {
    try {
      res.setHeader('Content-Type', metricsRegistry.register.contentType);
      const metrics = await metricsRegistry.register.metrics();
      res.status(200).send(metrics);
    } catch (err) {
      res.status(500).send({ error: 'Failed to scrape metrics' });
    }
  });

  // Health check endpoint
  app.get('/api/health', (_req: Request, res: Response) => {
    res.json({
      status: 'healthy',
      component: 'monitoring-service',
      uptime: process.uptime(),
      timestamp: Date.now(),
    });
  });

  // Real-time summary endpoint for Web Dashboards & Monitor Wall
  app.get('/api/metrics/summary', (_req: Request, res: Response) => {
    res.json(collector.getSummary());
  });

  // REST ingestion endpoint for direct stream telemetry pushes
  app.post('/api/telemetry/stream', (req: Request, res: Response) => {
    const streamMetrics = req.body;
    if (!streamMetrics || !streamMetrics.cameraId) {
      res.status(400).json({ error: 'Missing cameraId' });
      return;
    }
    collector.applyStreamMetrics(streamMetrics);
    res.json({ status: 'ok' });
  });

  // REST ingestion endpoint for system metrics pushes
  app.post('/api/telemetry/system', (req: Request, res: Response) => {
    const systemMetrics = req.body;
    if (!systemMetrics || !systemMetrics.machineId) {
      res.status(400).json({ error: 'Missing machineId' });
      return;
    }
    collector.applySystemMetrics(systemMetrics);
    res.json({ status: 'ok' });
  });

  return app;
}
