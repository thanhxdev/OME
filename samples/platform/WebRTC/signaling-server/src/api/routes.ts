import { Router, Request, Response } from 'express';
import { DEFAULT_ICE_SERVERS } from '@webrtc-broadcast/shared';
import { SessionManager } from '../session/session-manager';
import { generateToken, authenticateJwt, requireRole, AuthenticatedRequest } from '../auth/jwt';

export function createApiRouter(sessionManager: SessionManager): Router {
  const router = Router();

  // Authentication endpoint: issue token for roles (admin, encoder, decoder, monitor)
  router.post('/auth/token', (req: Request, res: Response) => {
    const { userId, role, sessionId, cameraId } = req.body;
    if (!userId || !role) {
      res.status(400).json({ error: 'userId and role are required' });
      return;
    }

    const token = generateToken({ userId, role, sessionId, cameraId });
    res.json({
      token,
      expiresIn: '24h',
      user: { userId, role, sessionId, cameraId },
    });
  });

  // Get ICE Servers config (STUN/TURN)
  router.get('/ice-servers', (_req: Request, res: Response) => {
    res.json({ iceServers: DEFAULT_ICE_SERVERS });
  });

  // Health check endpoint
  router.get('/health', (_req: Request, res: Response) => {
    res.json({
      status: 'healthy',
      uptime: process.uptime(),
      timestamp: new Date().toISOString(),
      activeSessions: sessionManager.listSessions().length,
    });
  });

  // Session CRUD
  router.get('/sessions', (_req: Request, res: Response) => {
    res.json(sessionManager.listSessions());
  });

  router.post('/sessions', (req: Request, res: Response) => {
    try {
      const { id, name, status, cameras } = req.body;
      if (!id || !name) {
        res.status(400).json({ error: 'Session id and name are required' });
        return;
      }
      const session = sessionManager.createSession({ id, name, status, cameras });
      res.status(201).json(session);
    } catch (err: any) {
      res.status(400).json({ error: err.message });
    }
  });

  router.get('/sessions/:id', (req: Request, res: Response) => {
    const session = sessionManager.getSession(req.params.id);
    if (!session) {
      res.status(404).json({ error: 'Session not found' });
      return;
    }
    res.json({
      ...session,
      cameraStatuses: sessionManager.getCameraStatuses(session.id),
      presences: sessionManager.getPresences(session.id),
    });
  });

  router.put('/sessions/:id', (req: Request, res: Response) => {
    try {
      const updated = sessionManager.updateSession(req.params.id, req.body);
      res.json(updated);
    } catch (err: any) {
      res.status(404).json({ error: err.message });
    }
  });

  router.delete('/sessions/:id', (req: Request, res: Response) => {
    const success = sessionManager.deleteSession(req.params.id);
    if (!success) {
      res.status(404).json({ error: 'Session not found' });
      return;
    }
    res.json({ success: true, message: `Session ${req.params.id} deleted` });
  });

  router.get('/sessions/:id/cameras', (req: Request, res: Response) => {
    const session = sessionManager.getSession(req.params.id);
    if (!session) {
      res.status(404).json({ error: 'Session not found' });
      return;
    }
    res.json(sessionManager.getCameraStatuses(session.id));
  });

  router.post('/sessions/:id/cameras', (req: Request, res: Response) => {
    try {
      const camera = sessionManager.addCameraToSession(req.params.id, req.body);
      res.status(201).json(camera);
    } catch (err: any) {
      res.status(400).json({ error: err.message });
    }
  });

  return router;
}
