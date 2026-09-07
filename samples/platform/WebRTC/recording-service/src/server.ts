import express, { Request, Response } from 'express';
import cors from 'cors';
import { IsoTrackRecorder } from './recorder';
import { StorageUploader } from './uploader';

export function createRecordingServer(recorder: IsoTrackRecorder, uploader: StorageUploader) {
  const app = express();

  app.use(cors());
  app.use(express.json());

  // Health check
  app.get('/api/health', (_req: Request, res: Response) => {
    res.json({
      status: 'healthy',
      component: 'recording-service',
      uptime: process.uptime(),
      timestamp: Date.now(),
    });
  });

  // Start ISO recording
  app.post('/api/recording/start', async (req: Request, res: Response) => {
    const { cameraId = 'all' } = req.body;
    const ok = await recorder.startRecording(cameraId);
    res.json({
      status: ok ? 'started' : 'already_running',
      target: cameraId,
      timestamp: Date.now(),
    });
  });

  // Stop ISO recording
  app.post('/api/recording/stop', async (req: Request, res: Response) => {
    const { cameraId = 'all' } = req.body;
    const ok = await recorder.stopRecording(cameraId);
    res.json({
      status: ok ? 'stopped' : 'not_running',
      target: cameraId,
      timestamp: Date.now(),
    });
  });

  // Get current recording status
  app.get('/api/recording/status', (req: Request, res: Response) => {
    const cameraId = req.query.cameraId as string | undefined;
    const status = recorder.getStatus(cameraId);
    res.json({
      timestamp: Date.now(),
      recordings: status,
    });
  });

  // List all recorded files & S3 upload states
  app.get('/api/recordings', (_req: Request, res: Response) => {
    const records = uploader.getRecords();
    res.json({
      count: records.length,
      timestamp: Date.now(),
      records,
    });
  });

  return app;
}
