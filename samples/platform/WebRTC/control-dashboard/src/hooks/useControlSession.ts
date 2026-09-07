import { useState, useEffect, useRef, useCallback } from 'react';
import {
  CameraStatus,
  SignalingMessage,
  CodecMode,
  TallyState,
} from '@webrtc-broadcast/shared';

export interface CameraControlState extends CameraStatus {
  resolution?: string;
}

export type GlobalPreset = 'max_quality' | 'balanced' | 'bandwidth_saver' | 'minimal' | 'custom';

export const PRESET_CONFIGS: Record<
  Exclude<GlobalPreset, 'custom'>,
  { name: string; codec: CodecMode; bitrate: number; fps: number; desc: string }
> = {
  max_quality: {
    name: 'Max Quality',
    codec: 'throughpass',
    bitrate: 14000,
    fps: 60,
    desc: 'Uncompressed SDI throughpass, maximum fidelity for stadium highlights',
  },
  balanced: {
    name: 'Balanced Broadcast',
    codec: 'h264',
    bitrate: 8500,
    fps: 60,
    desc: 'Industry standard 1080p60 H.264, optimized for low latency & reliability',
  },
  bandwidth_saver: {
    name: 'Bandwidth Saver',
    codec: 'h265',
    bitrate: 4500,
    fps: 60,
    desc: 'HEVC / H.265 high compression, 50% bandwidth saving on congested uplink',
  },
  minimal: {
    name: 'Minimal Uplink',
    codec: 'h265',
    bitrate: 2500,
    fps: 30,
    desc: 'Emergency low-bitrate profile for cellular or heavily restricted WAN',
  },
};

const INITIAL_CAMERAS: CameraControlState[] = [
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

export function useControlSession(wsUrl = 'ws://localhost:3000/ws') {
  const [cameras, setCameras] = useState<CameraControlState[]>(INITIAL_CAMERAS);
  const [connected, setConnected] = useState(false);
  const [activePreset, setActivePreset] = useState<GlobalPreset>('balanced');
  const [programCam, setProgramCam] = useState<string>('cam-01');
  const [previewCam, setPreviewCam] = useState<string>('cam-02');
  const [intercomActive, setIntercomActive] = useState(false);
  const [intercomTarget, setIntercomTarget] = useState<string>('all');
  const wsRef = useRef<WebSocket | null>(null);

  // Send WebSocket message helper
  const sendMessage = useCallback((msg: SignalingMessage) => {
    if (wsRef.current && wsRef.current.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify(msg));
    }
  }, []);

  // Update Tally state for a camera
  const setCameraTally = useCallback(
    (cameraId: string, state: TallyState) => {
      setCameras((prev) =>
        prev.map((c) => (c.id === cameraId ? { ...c, tally: state } : c))
      );

      if (state === 'on-air') {
        setProgramCam(cameraId);
      } else if (state === 'preview') {
        setPreviewCam(cameraId);
      }

      sendMessage({
        type: 'tally_update',
        sessionId: 'event-001',
        senderId: 'control-dashboard',
        timestamp: Date.now(),
        cameraId,
        state,
      });
    },
    [sendMessage]
  );

  // Change individual camera codec / bitrate / fps
  const setCameraCodecConfig = useCallback(
    (cameraId: string, codec: CodecMode, bitrate?: number, fps?: number) => {
      setActivePreset('custom');
      setCameras((prev) =>
        prev.map((c) => {
          if (c.id === cameraId) {
            return {
              ...c,
              codec,
              currentBitrate: bitrate ?? c.currentBitrate,
              fps: fps ?? c.fps,
            };
          }
          return c;
        })
      );

      sendMessage({
        type: 'codec_change',
        sessionId: 'event-001',
        senderId: 'control-dashboard',
        timestamp: Date.now(),
        cameraId,
        codec,
        bitrate,
        fps,
      });
    },
    [sendMessage]
  );

  // Apply Global Codec Preset to ALL cameras
  const applyGlobalPreset = useCallback(
    (preset: Exclude<GlobalPreset, 'custom'>) => {
      setActivePreset(preset);
      const conf = PRESET_CONFIGS[preset];

      setCameras((prev) =>
        prev.map((c) => ({
          ...c,
          codec: conf.codec,
          currentBitrate: conf.bitrate,
          fps: conf.fps,
        }))
      );

      sendMessage({
        type: 'codec_change',
        sessionId: 'event-001',
        senderId: 'control-dashboard',
        timestamp: Date.now(),
        cameraId: 'all',
        codec: conf.codec,
        bitrate: conf.bitrate,
        fps: conf.fps,
      });
    },
    [sendMessage]
  );

  // Tally TAKE: Swap Program and Preview
  const executeTake = useCallback(() => {
    const currentProg = programCam;
    const currentPrev = previewCam;

    setProgramCam(currentPrev);
    setPreviewCam(currentProg);

    setCameras((prev) =>
      prev.map((c) => {
        if (c.id === currentPrev) return { ...c, tally: 'on-air' };
        if (c.id === currentProg) return { ...c, tally: 'preview' };
        return c;
      })
    );

    sendMessage({
      type: 'tally_update',
      sessionId: 'event-001',
      senderId: 'control-dashboard',
      timestamp: Date.now(),
      cameraId: currentPrev,
      state: 'on-air',
    });

    sendMessage({
      type: 'tally_update',
      sessionId: 'event-001',
      senderId: 'control-dashboard',
      timestamp: Date.now(),
      cameraId: currentProg,
      state: 'preview',
    });
  }, [programCam, previewCam, sendMessage]);

  // Tally CUT: Clear Preview, Cut Program directly to selected
  const executeCut = useCallback(
    (targetCamId: string) => {
      const oldProg = programCam;
      setProgramCam(targetCamId);

      setCameras((prev) =>
        prev.map((c) => {
          if (c.id === targetCamId) return { ...c, tally: 'on-air' };
          if (c.id === oldProg && oldProg !== targetCamId) return { ...c, tally: 'off' };
          return c;
        })
      );

      sendMessage({
        type: 'tally_update',
        sessionId: 'event-001',
        senderId: 'control-dashboard',
        timestamp: Date.now(),
        cameraId: targetCamId,
        state: 'on-air',
      });

      if (oldProg !== targetCamId) {
        sendMessage({
          type: 'tally_update',
          sessionId: 'event-001',
          senderId: 'control-dashboard',
          timestamp: Date.now(),
          cameraId: oldProg,
          state: 'off',
        });
      }
    },
    [programCam, sendMessage]
  );

  // Push-to-talk intercom trigger
  const setIntercomPtt = useCallback(
    (active: boolean) => {
      setIntercomActive(active);
      sendMessage({
        type: 'intercom_audio',
        sessionId: 'event-001',
        senderId: 'control-dashboard',
        timestamp: Date.now(),
        from: 'producer-main',
        to: intercomTarget,
        audioData: active ? 'PTT_ACTIVE_STREAM' : 'PTT_RELEASED',
      });
    },
    [intercomTarget, sendMessage]
  );

  // Background WebSocket connection & sync
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
            senderId: `ctrl-${Date.now().toString(36)}`,
            timestamp: Date.now(),
            role: 'admin',
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
              if (msg.state === 'on-air') setProgramCam(msg.cameraId);
              if (msg.state === 'preview') setPreviewCam(msg.cameraId);
            }
          } catch {
            // ignore
          }
        };

        ws.onclose = () => {
          setConnected(false);
          reconnectTimer = setTimeout(connect, 3000);
        };

        ws.onerror = () => ws.close();
      } catch {
        reconnectTimer = setTimeout(connect, 3000);
      }
    };

    connect();

    return () => {
      clearTimeout(reconnectTimer);
      if (wsRef.current) wsRef.current.close();
    };
  }, [wsUrl]);

  // Compute total WAN uplink bitrate
  const totalBitrateKbps = cameras.reduce(
    (acc, c) => acc + (c.online ? c.currentBitrate : 0),
    0
  );

  return {
    connected,
    cameras,
    activePreset,
    programCam,
    previewCam,
    intercomActive,
    intercomTarget,
    totalBitrateKbps,
    setIntercomTarget,
    setIntercomPtt,
    applyGlobalPreset,
    setCameraCodecConfig,
    setCameraTally,
    setProgramCam,
    setPreviewCam,
    executeTake,
    executeCut,
  };
}
