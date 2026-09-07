import { useState, useEffect, useRef, useCallback } from 'react';
import { CameraStatus, SignalingMessage } from '@webrtc-broadcast/shared';

export interface CameraStreamState extends CameraStatus {
  jitter?: number;
  droppedFrames?: number;
  resolution?: string;
}

export interface SignalingState {
  connected: boolean;
  cameras: CameraStreamState[];
  totalWanTxKbps: number;
  cpuLoad: number;
  activeSession: string;
}

const DEFAULT_CAMERAS: CameraStreamState[] = [
  { id: 'cam-01', name: 'Main Wide 50m', online: true, codec: 'h264', currentBitrate: 8500, fps: 60, packetLoss: 0.1, rtt: 18, tally: 'on-air', resolution: '1920x1080' },
  { id: 'cam-02', name: 'Goal Box Left', online: true, codec: 'h264', currentBitrate: 8500, fps: 60, packetLoss: 0.2, rtt: 21, tally: 'preview', resolution: '1920x1080' },
  { id: 'cam-03', name: 'Goal Box Right', online: true, codec: 'h264', currentBitrate: 8500, fps: 60, packetLoss: 0.1, rtt: 20, tally: 'off', resolution: '1920x1080' },
  { id: 'cam-04', name: 'Bench Home', online: true, codec: 'h264', currentBitrate: 6500, fps: 60, packetLoss: 0.0, rtt: 16, tally: 'off', resolution: '1920x1080' },
  { id: 'cam-05', name: 'Bench Away', online: true, codec: 'h264', currentBitrate: 6500, fps: 60, packetLoss: 0.0, rtt: 19, tally: 'off', resolution: '1920x1080' },
  { id: 'cam-06', name: 'Spidercam High', online: true, codec: 'throughpass', currentBitrate: 14000, fps: 60, packetLoss: 0.4, rtt: 24, tally: 'off', resolution: '1920x1080' },
  { id: 'cam-07', name: 'Close-up Center', online: true, codec: 'h265', currentBitrate: 5500, fps: 60, packetLoss: 0.1, rtt: 22, tally: 'off', resolution: '1920x1080' },
  { id: 'cam-08', name: 'Tactical Aerial', online: true, codec: 'h265', currentBitrate: 5500, fps: 60, packetLoss: 0.0, rtt: 17, tally: 'off', resolution: '1920x1080' },
  { id: 'cam-09', name: 'Tunnel Entry', online: true, codec: 'h264', currentBitrate: 6500, fps: 60, packetLoss: 0.1, rtt: 25, tally: 'off', resolution: '1920x1080' },
  { id: 'cam-10', name: 'Beauty Exterior', online: true, codec: 'h264', currentBitrate: 7000, fps: 60, packetLoss: 0.0, rtt: 19, tally: 'off', resolution: '1920x1080' },
];

export function useSignaling(wsUrl = 'ws://localhost:3000/ws') {
  const [cameras, setCameras] = useState<CameraStreamState[]>(DEFAULT_CAMERAS);
  const [connected, setConnected] = useState(false);
  const [totalWanTxKbps, setTotalWanTxKbps] = useState(77000);
  const [cpuLoad, setCpuLoad] = useState(24);
  const [activeSession, setActiveSession] = useState('event-001');
  const wsRef = useRef<WebSocket | null>(null);

  // Poll monitoring-service summary endpoint as background sync or fallback
  const fetchSummary = useCallback(async () => {
    try {
      const res = await fetch('http://localhost:9100/api/metrics/summary');
      if (res.ok) {
        const data = await res.json();
        if (Array.isArray(data.cameras) && data.cameras.length > 0) {
          setCameras(data.cameras.map((c: any) => ({
            id: c.id,
            name: c.name,
            online: c.online,
            codec: c.codec,
            currentBitrate: c.bitrate || 0,
            fps: c.fps || 0,
            packetLoss: c.packetLoss || 0,
            rtt: c.rtt || 0,
            jitter: c.jitter || 0,
            droppedFrames: c.droppedFrames || 0,
            tally: c.tally || 'off',
            resolution: c.resolution || '1920x1080',
          })));
        }
        if (data.totalWanTxKbps !== undefined) setTotalWanTxKbps(data.totalWanTxKbps);
        if (data.systemMetrics?.cpu !== undefined) setCpuLoad(data.systemMetrics.cpu);
      }
    } catch {
      // Ignore fetch errors if monitoring service is cold
    }
  }, []);

  useEffect(() => {
    let reconnectTimer: any;

    const connect = () => {
      try {
        const ws = new WebSocket(wsUrl);
        wsRef.current = ws;

        ws.onopen = () => {
          setConnected(true);
          const joinMsg: SignalingMessage = {
            type: 'join_session',
            sessionId: 'event-001',
            senderId: `wall-${Date.now().toString(36)}`,
            timestamp: Date.now(),
            role: 'monitor',
          };
          ws.send(JSON.stringify(joinMsg));
        };

        ws.onmessage = (event) => {
          try {
            const msg: SignalingMessage = JSON.parse(event.data);
            if (msg.type === 'joined_session' || msg.type === 'session_sync') {
              const camList = (msg as any).cameras;
              if (Array.isArray(camList) && camList.length > 0) {
                setCameras((prev) =>
                  prev.map((c) => {
                    const match = camList.find((x: any) => x.id === c.id);
                    return match ? { ...c, ...match } : c;
                  })
                );
              }
            } else if (msg.type === 'tally_update') {
              setCameras((prev) =>
                prev.map((c) => (c.id === msg.cameraId ? { ...c, tally: msg.state } : c))
              );
            } else if (msg.type === 'codec_changed' || msg.type === 'codec_change') {
              const target = msg.cameraId;
              setCameras((prev) =>
                prev.map((c) => {
                  if (target === 'all' || target === c.id) {
                    return {
                      ...c,
                      codec: msg.codec,
                      currentBitrate: msg.bitrate || c.currentBitrate,
                      fps: msg.fps || c.fps,
                    };
                  }
                  return c;
                })
              );
            }
          } catch {
            // invalid message format ignored
          }
        };

        ws.onclose = () => {
          setConnected(false);
          reconnectTimer = setTimeout(connect, 3000);
        };

        ws.onerror = () => {
          ws.close();
        };
      } catch {
        reconnectTimer = setTimeout(connect, 3000);
      }
    };

    connect();
    const pollTimer = setInterval(fetchSummary, 2000);

    return () => {
      clearTimeout(reconnectTimer);
      clearInterval(pollTimer);
      if (wsRef.current) wsRef.current.close();
    };
  }, [wsUrl, fetchSummary]);

  return {
    connected,
    cameras,
    totalWanTxKbps,
    cpuLoad,
    activeSession,
  };
}
