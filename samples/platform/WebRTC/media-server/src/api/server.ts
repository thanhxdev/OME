import { Router, Request, Response } from 'express';
import { RtpRouter } from '../sfu/rtp-router';

export function createMediaApiRouter(router: RtpRouter): Router {
  const api = Router();

  // Health probe
  api.get('/health', (_req: Request, res: Response) => {
    res.json({
      status: 'healthy',
      component: 'sfu',
      uptime: process.uptime(),
      activeProducers: router.getAllProducers().length,
      timestamp: new Date().toISOString(),
    });
  });

  // List all streams & live statistics
  api.get('/streams', (_req: Request, res: Response) => {
    res.json(router.statsTracker.getAllStats());
  });

  // Get specific camera statistics
  api.get('/streams/:id/stats', (req: Request, res: Response) => {
    const stats = router.statsTracker.getStats(req.params.id);
    if (!stats) {
      res.status(404).json({ error: `Stream ${req.params.id} not found` });
      return;
    }
    res.json(stats);
  });

  // Subscribe to a camera stream (Studio Decoder, Monitor Wall, or Recording Service)
  api.post('/streams/:id/subscribe', (req: Request, res: Response) => {
    const cameraId = req.params.id;
    const { id, role, ip, videoPort, audioPort } = req.body;

    if (!id || !ip || !videoPort) {
      res.status(400).json({ error: 'id, ip, and videoPort are required' });
      return;
    }

    router.addSubscriber(cameraId, {
      id,
      role: role || 'subscriber',
      ip,
      videoPort: parseInt(videoPort, 10),
      audioPort: audioPort ? parseInt(audioPort, 10) : undefined,
      joinedAt: Date.now(),
    });

    const producer = router.getProducer(cameraId);
    res.status(201).json({
      success: true,
      cameraId,
      subscriberId: id,
      ingressVideoPort: producer?.videoPort,
      ingressAudioPort: producer?.audioPort,
    });
  });

  // Dynamically allocate ingress UDP port(s) for a camera stream
  api.post('/streams/:id/allocate', async (req: Request, res: Response) => {
    const cameraId = req.params.id;
    const isSinglePort = req.body.isSinglePort === true;
    const codec = req.body.codec || 'h264';

    try {
      const producer = await router.allocateCamera(cameraId, isSinglePort, codec);
      res.status(200).json({
        success: true,
        cameraId,
        videoPort: producer.videoPort,
        audioPort: producer.audioPort,
        isSinglePort: !!producer.isSinglePort,
      });
    } catch (err: any) {
      res.status(500).json({
        success: false,
        error: `Failed to allocate ports for camera ${cameraId}: ${err?.message || err}`,
      });
    }
  });

  // Unsubscribe (supports both /subscribe/:subId and /subscribers/:subId)
  const handleUnsubscribe = (req: Request, res: Response) => {
    router.removeSubscriber(req.params.id, req.params.subId);
    res.json({ success: true, message: `Subscriber ${req.params.subId} removed` });
  };
  api.delete('/streams/:id/subscribe/:subId', handleUnsubscribe);
  api.delete('/streams/:id/subscribers/:subId', handleUnsubscribe);

  // List all subscribers
  api.get('/subscribers', (_req: Request, res: Response) => {
    const allSubs: any[] = [];
    for (const p of router.getAllProducers()) {
      for (const sub of p.subscribers.values()) {
        allSubs.push({
          cameraId: p.cameraId,
          ...sub,
        });
      }
    }
    res.json(allSubs);
  });

  return api;
}
