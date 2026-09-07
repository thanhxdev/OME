import fs from 'fs';
import path from 'path';
import dgram from 'dgram';
import http from 'http';
import { EventEmitter } from 'events';
import { config } from './config';

export interface RecordedSegment {
  cameraId: string;
  filename: string;
  filePath: string;
  partNumber: number;
  startTime: number;
  endTime?: number;
  durationSeconds: number;
  fileSizeBytes: number;
  sha256?: string;
}

export interface CameraRecordingState {
  cameraId: string;
  isRecording: boolean;
  startTime?: number;
  durationSeconds: number;
  fileSizeBytes: number;
  currentFilename?: string;
  currentFilePath?: string;
  partNumber: number;
  videoPort: number;
  audioPort: number;
  packetsCaptured: number;
}

export class IsoTrackRecorder extends EventEmitter {
  private cameras: Map<string, CameraRecordingState> = new Map();
  private sockets: Map<string, { video: dgram.Socket; audio: dgram.Socket }> = new Map();
  private fileStreams: Map<string, fs.WriteStream> = new Map();
  private segmentTimers: Map<string, NodeJS.Timeout> = new Map();
  private tickerTimer: NodeJS.Timeout | null = null;

  constructor() {
    super();

    // Ensure recordings directory exists
    if (!fs.existsSync(config.recordingsDir)) {
      fs.mkdirSync(config.recordingsDir, { recursive: true });
    }

    // Initialize 10 camera states
    for (let i = 1; i <= 10; i++) {
      const id = `cam-${String(i).padStart(2, '0')}`;
      const videoPort = config.baseRtpPort + (i - 1) * 4;
      const audioPort = videoPort + 2;

      this.cameras.set(id, {
        cameraId: id,
        isRecording: false,
        durationSeconds: 0,
        fileSizeBytes: 0,
        partNumber: 0,
        videoPort,
        audioPort,
        packetsCaptured: 0,
      });
    }

    // Start 1-second update ticker
    this.tickerTimer = setInterval(() => this.updateTickers(), 1000);
  }

  public async startRecording(cameraId: string): Promise<boolean> {
    if (cameraId === 'all') {
      let anyStarted = false;
      for (const id of this.cameras.keys()) {
        const ok = await this.startSingleCamera(id);
        if (ok) anyStarted = true;
      }
      return anyStarted;
    } else {
      return this.startSingleCamera(cameraId);
    }
  }

  public async stopRecording(cameraId: string): Promise<boolean> {
    if (cameraId === 'all') {
      for (const id of this.cameras.keys()) {
        await this.stopSingleCamera(id);
      }
      return true;
    } else {
      return this.stopSingleCamera(cameraId);
    }
  }

  private async startSingleCamera(cameraId: string): Promise<boolean> {
    const cam = this.cameras.get(cameraId);
    if (!cam || cam.isRecording) return false;

    cam.isRecording = true;
    cam.startTime = Date.now();
    cam.durationSeconds = 0;
    cam.fileSizeBytes = 0;
    cam.packetsCaptured = 0;
    cam.partNumber = 1;

    // Start a new segment file
    this.openNewSegment(cam);

    // Bind UDP receivers for Video & Audio RTP
    this.bindUdpReceivers(cam);

    // Register as subscriber to SFU
    await this.subscribeToSfu(cam);

    // Schedule rolling segment splitting
    this.scheduleSegmentRotation(cam);

    this.emit('recording_started', cam);
    return true;
  }

  private async stopSingleCamera(cameraId: string): Promise<boolean> {
    const cam = this.cameras.get(cameraId);
    if (!cam || !cam.isRecording) return false;

    cam.isRecording = false;

    // Clear segment timer
    const timer = this.segmentTimers.get(cameraId);
    if (timer) {
      clearTimeout(timer);
      this.segmentTimers.delete(cameraId);
    }

    // Close and flush current segment file
    this.closeCurrentSegment(cam);

    // Unbind UDP receivers
    this.unbindUdpReceivers(cam);

    // Unsubscribe from SFU
    await this.unsubscribeFromSfu(cam);

    this.emit('recording_stopped', cam);
    return true;
  }

  private openNewSegment(cam: CameraRecordingState): void {
    const now = new Date();
    const dateStr = now.toISOString().replace(/[-:T]/g, '').slice(0, 14);
    const filename = `${cam.cameraId}_${dateStr}_part${String(cam.partNumber).padStart(3, '0')}.mp4`;
    const filePath = path.join(config.recordingsDir, filename);

    cam.currentFilename = filename;
    cam.currentFilePath = filePath;
    cam.fileSizeBytes = 0;

    const fileStream = fs.createWriteStream(filePath, { flags: 'a' });
    this.fileStreams.set(cam.cameraId, fileStream);
  }

  private closeCurrentSegment(cam: CameraRecordingState): void {
    const stream = this.fileStreams.get(cam.cameraId);
    if (stream) {
      stream.end();
      this.fileStreams.delete(cam.cameraId);

      if (cam.currentFilePath && fs.existsSync(cam.currentFilePath)) {
        const stats = fs.statSync(cam.currentFilePath);
        const segment: RecordedSegment = {
          cameraId: cam.cameraId,
          filename: cam.currentFilename || path.basename(cam.currentFilePath),
          filePath: cam.currentFilePath,
          partNumber: cam.partNumber,
          startTime: cam.startTime || Date.now(),
          endTime: Date.now(),
          durationSeconds: cam.durationSeconds,
          fileSizeBytes: stats.size,
        };
        this.emit('segment_completed', segment);
      }
    }
  }

  private rotateSegment(cam: CameraRecordingState): void {
    if (!cam.isRecording) return;
    this.closeCurrentSegment(cam);
    cam.partNumber++;
    this.openNewSegment(cam);
    this.scheduleSegmentRotation(cam);
    this.emit('segment_rotated', cam);
  }

  private scheduleSegmentRotation(cam: CameraRecordingState): void {
    if (this.segmentTimers.has(cam.cameraId)) {
      clearTimeout(this.segmentTimers.get(cam.cameraId)!);
    }
    const timer = setTimeout(() => {
      this.rotateSegment(cam);
    }, config.segmentDurationSeconds * 1000);
    this.segmentTimers.set(cam.cameraId, timer);
  }

  private bindUdpReceivers(cam: CameraRecordingState): void {
    try {
      const videoSocket = dgram.createSocket('udp4');
      videoSocket.on('message', (msg: Buffer) => {
        cam.packetsCaptured++;
        cam.fileSizeBytes += msg.length;
        const stream = this.fileStreams.get(cam.cameraId);
        if (stream && !stream.destroyed) {
          stream.write(msg);
        }
      });
      videoSocket.bind(cam.videoPort, '127.0.0.1');

      const audioSocket = dgram.createSocket('udp4');
      audioSocket.on('message', (msg: Buffer) => {
        cam.fileSizeBytes += msg.length;
        const stream = this.fileStreams.get(cam.cameraId);
        if (stream && !stream.destroyed) {
          stream.write(msg);
        }
      });
      audioSocket.bind(cam.audioPort, '127.0.0.1');

      this.sockets.set(cam.cameraId, { video: videoSocket, audio: audioSocket });
    } catch {
      // socket binding error fallback
    }
  }

  private unbindUdpReceivers(cam: CameraRecordingState): void {
    const s = this.sockets.get(cam.cameraId);
    if (s) {
      try {
        s.video.close();
        s.audio.close();
      } catch {}
      this.sockets.delete(cam.cameraId);
    }
  }

  private async subscribeToSfu(cam: CameraRecordingState): Promise<void> {
    try {
      await this.postJson(`${config.sfuHttpUrl}/api/streams/${cam.cameraId}/subscribe`, {
        id: `recorder-${cam.cameraId}`,
        role: 'recording',
        ip: '127.0.0.1',
        videoPort: cam.videoPort,
        audioPort: cam.audioPort,
      });
    } catch {}
  }

  private async unsubscribeFromSfu(cam: CameraRecordingState): Promise<void> {
    try {
      await this.postJson(`${config.sfuHttpUrl}/api/streams/${cam.cameraId}/unsubscribe`, {
        id: `recorder-${cam.cameraId}`,
      });
    } catch {}
  }

  private updateTickers(): void {
    for (const cam of this.cameras.values()) {
      if (cam.isRecording && cam.startTime) {
        cam.durationSeconds = Math.floor((Date.now() - cam.startTime) / 1000);
      }
    }
  }

  public getStatus(cameraId?: string): CameraRecordingState | CameraRecordingState[] {
    if (cameraId && cameraId !== 'all') {
      return (
        this.cameras.get(cameraId) || {
          cameraId,
          isRecording: false,
          durationSeconds: 0,
          fileSizeBytes: 0,
          partNumber: 0,
          videoPort: 0,
          audioPort: 0,
          packetsCaptured: 0,
        }
      );
    }
    return Array.from(this.cameras.values());
  }

  private postJson(urlStr: string, payload: any): Promise<any> {
    return new Promise((resolve) => {
      try {
        const u = new URL(urlStr);
        const body = JSON.stringify(payload);
        const req = http.request(
          {
            hostname: u.hostname,
            port: u.port,
            path: u.pathname,
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'Content-Length': Buffer.byteLength(body),
            },
            timeout: 2000,
          },
          (res) => {
            let data = '';
            res.on('data', (d) => (data += d));
            res.on('end', () => {
              try {
                resolve(JSON.parse(data));
              } catch {
                resolve({ status: 'ok' });
              }
            });
          }
        );
        req.on('error', () => resolve({ error: 'connection failed' }));
        req.write(body);
        req.end();
      } catch {
        resolve({ error: 'invalid url' });
      }
    });
  }

  public shutdown(): void {
    if (this.tickerTimer) clearInterval(this.tickerTimer);
    for (const cam of this.cameras.values()) {
      if (cam.isRecording) {
        this.stopSingleCamera(cam.cameraId);
      }
    }
  }
}
