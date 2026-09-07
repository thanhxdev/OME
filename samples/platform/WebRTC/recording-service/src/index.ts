import WebSocket from 'ws';
import { SignalingMessage, RecordingCommandMessage, RecordingStatusMessage } from '@webrtc-broadcast/shared';
import { config } from './config';
import { IsoTrackRecorder } from './recorder';
import { StorageUploader } from './uploader';
import { createRecordingServer } from './server';

async function main() {
  console.log('====================================================');
  console.log(' WebRTC 10-Camera ISO Multi-Track Recording Service');
  console.log('====================================================');
  console.log(`HTTP Port:        ${config.port}`);
  console.log(`Signaling URL:    ${config.signalingWsUrl}`);
  console.log(`SFU URL:          ${config.sfuHttpUrl}`);
  console.log(`Session ID:       ${config.sessionId}`);
  console.log(`Recordings Dir:   ${config.recordingsDir}`);
  console.log(`Segment Duration: ${config.segmentDurationSeconds}s`);
  console.log(`S3 Endpoint:      ${config.s3Endpoint}/${config.s3Bucket}`);
  console.log('----------------------------------------------------');

  const recorder = new IsoTrackRecorder();
  const uploader = new StorageUploader();

  // Pipe completed segments from recorder directly to storage uploader
  recorder.on('segment_completed', (segment) => {
    console.log(`[Recorder] Segment completed: ${segment.filename} (${(segment.fileSizeBytes / 1024).toFixed(1)} KB)`);
    uploader.enqueue(segment);
  });

  // Signaling WebSocket Client
  let ws: WebSocket | null = null;
  let isRunning = true;

  const connectSignaling = () => {
    if (!isRunning) return;

    try {
      ws = new WebSocket(config.signalingWsUrl);

      ws.on('open', () => {
        console.log('[Recorder] Connected to Signaling Server');
        const joinMsg: SignalingMessage = {
          type: 'join_session',
          sessionId: config.sessionId,
          senderId: 'iso-recorder-service',
          timestamp: Date.now(),
          role: 'admin',
        };
        ws?.send(JSON.stringify(joinMsg));
      });

      ws.on('message', async (data: WebSocket.RawData) => {
        try {
          const msg = JSON.parse(data.toString()) as SignalingMessage;
          if (msg.type === 'recording_command') {
            const cmd = msg as RecordingCommandMessage;
            console.log(`[Recorder] Received recording command: ${cmd.action} for camera ${cmd.cameraId}`);
            if (cmd.action === 'start') {
              await recorder.startRecording(cmd.cameraId);
            } else if (cmd.action === 'stop') {
              await recorder.stopRecording(cmd.cameraId);
            }
          }
        } catch {}
      });

      ws.on('close', () => {
        setTimeout(connectSignaling, 3000);
      });

      ws.on('error', () => {
        ws?.close();
      });
    } catch {
      setTimeout(connectSignaling, 3000);
    }
  };

  connectSignaling();

  // Broadcast recording status changes to signaling server
  const broadcastStatus = (camId: string, isRec: boolean, duration = 0, size = 0, file = '') => {
    if (ws && ws.readyState === WebSocket.OPEN) {
      const statusMsg: RecordingStatusMessage = {
        type: 'recording_status',
        sessionId: config.sessionId,
        senderId: 'iso-recorder-service',
        timestamp: Date.now(),
        cameraId: camId,
        isRecording: isRec,
        durationSeconds: duration,
        fileSizeBytes: size,
        currentFilename: file,
      };
      ws.send(JSON.stringify(statusMsg));
    }
  };

  recorder.on('recording_started', (cam) => {
    broadcastStatus(cam.cameraId, true, 0, 0, cam.currentFilename);
  });

  recorder.on('recording_stopped', (cam) => {
    broadcastStatus(cam.cameraId, false, cam.durationSeconds, cam.fileSizeBytes, cam.currentFilename);
  });

  // Start HTTP REST Server
  const app = createRecordingServer(recorder, uploader);
  const server = app.listen(config.port, config.host, () => {
    console.log(`[Recorder] HTTP API listening on http://${config.host}:${config.port}`);
  });

  const shutdown = () => {
    console.log('\n[Recorder] Initiating graceful shutdown...');
    isRunning = false;
    recorder.shutdown();
    if (ws) ws.close();
    server.close(() => {
      console.log('[Recorder] Server shut down successfully.');
      process.exit(0);
    });
  };

  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
}

if (require.main === module) {
  main().catch((err) => {
    console.error('[Recorder] Fatal initialization error:', err);
    process.exit(1);
  });
}
