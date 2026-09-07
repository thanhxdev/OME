import WebSocket from 'ws';
import {
  SignalingMessage,
  TallyUpdateMessage,
  CodecChangeMessage,
  IntercomAudioMessage,
  HealthReportMessage,
} from '@webrtc-broadcast/shared';
import { AgentConfig } from './config';
import { ProcessSupervisor } from './process-supervisor';
import { HardwareTelemetry } from './hardware-telemetry';
import { GpioTallyController } from './gpio-tally';
import { IntercomClient } from './intercom-client';

export class EncoderControlAgent {
  private supervisor: ProcessSupervisor;
  private telemetry: HardwareTelemetry;
  private tally: GpioTallyController;
  private intercom: IntercomClient;
  private ws: WebSocket | null = null;
  private isRunning = false;
  private telemetryTimer: NodeJS.Timeout | null = null;

  constructor(private config: AgentConfig) {
    this.supervisor = new ProcessSupervisor(config);
    this.telemetry = new HardwareTelemetry(`agent-${config.cameraId}`, config.cameraId);
    this.tally = new GpioTallyController(config.gpioOnAirPin, config.gpioPreviewPin, config.mockHardware);
    this.intercom = new IntercomClient(config.cameraId);

    this.supervisor.on('encoder_started', ({ pid }) => {
      console.log(`[Agent ${config.cameraId}] Encoder process active (PID: ${pid})`);
    });

    this.supervisor.on('watchdog_restarting', ({ attempt, backoffMs }) => {
      console.warn(`[Agent ${config.cameraId}] Watchdog restart attempt #${attempt} in ${backoffMs}ms`);
    });
  }

  public start(): void {
    if (this.isRunning) return;
    this.isRunning = true;

    // 1. Connect to Signaling Server
    this.connectSignaling();

    // 2. Start Encoder Supervisor
    this.supervisor.start();

    // 3. Start Telemetry Broadcast Loop
    this.telemetryTimer = setInterval(() => {
      this.sendHealthReport();
    }, this.config.telemetryIntervalMs);
  }

  public stop(): void {
    this.isRunning = false;
    if (this.telemetryTimer) clearInterval(this.telemetryTimer);
    this.supervisor.stop();
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }

  private connectSignaling(): void {
    if (!this.isRunning) return;

    try {
      this.ws = new WebSocket(this.config.signalingWsUrl);

      this.ws.on('open', () => {
        console.log(`[Agent ${this.config.cameraId}] Connected to signaling server`);

        // Join session as encoder
        const joinMsg: SignalingMessage = {
          type: 'join_session',
          sessionId: this.config.sessionId,
          senderId: `agent-${this.config.cameraId}`,
          timestamp: Date.now(),
          role: 'encoder',
          cameraId: this.config.cameraId,
        };
        this.ws?.send(JSON.stringify(joinMsg));
      });

      this.ws.on('message', (data: WebSocket.RawData) => {
        try {
          const msg = JSON.parse(data.toString()) as SignalingMessage;
          this.handleSignalingMessage(msg);
        } catch {}
      });

      this.ws.on('close', () => {
        setTimeout(() => this.connectSignaling(), 3000);
      });

      this.ws.on('error', () => {
        this.ws?.close();
      });
    } catch {
      setTimeout(() => this.connectSignaling(), 3000);
    }
  }

  private handleSignalingMessage(msg: SignalingMessage): void {
    switch (msg.type) {
      case 'tally_update': {
        const tallyMsg = msg as TallyUpdateMessage;
        if (tallyMsg.cameraId === this.config.cameraId) {
          console.log(`[Agent ${this.config.cameraId}] TALLY -> ${tallyMsg.state.toUpperCase()}`);
          this.tally.setTally(tallyMsg.state);
        }
        break;
      }

      case 'codec_change': {
        const codecMsg = msg as CodecChangeMessage;
        if (codecMsg.cameraId === 'all' || codecMsg.cameraId === this.config.cameraId) {
          console.log(
            `[Agent ${this.config.cameraId}] Dynamic Codec Switch: ${codecMsg.codec} (${codecMsg.bitrate || this.config.bitrate} kbps)`
          );
          this.config.codec = codecMsg.codec;
          if (codecMsg.bitrate) this.config.bitrate = codecMsg.bitrate;
          if (codecMsg.fps) this.config.fps = codecMsg.fps;

          this.supervisor.restartWithConfig({
            codec: this.config.codec,
            bitrate: this.config.bitrate,
            fps: this.config.fps,
          });
        }
        break;
      }

      case 'intercom_audio': {
        const audioMsg = msg as IntercomAudioMessage;
        if (audioMsg.to === 'all' || audioMsg.to === this.config.cameraId) {
          this.intercom.handleIncomingAudio(audioMsg.from, audioMsg.audioData);
        }
        break;
      }
    }
  }

  private sendHealthReport(): void {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;

    const streamMetrics = this.telemetry.sampleStreamMetrics(this.config.bitrate, this.config.fps);
    const systemMetrics = this.telemetry.sampleSystemMetrics();

    const report: HealthReportMessage = {
      type: 'health_report',
      sessionId: this.config.sessionId,
      senderId: `agent-${this.config.cameraId}`,
      timestamp: Date.now(),
      sourceId: this.config.cameraId,
      sourceType: 'encoder',
      streamMetrics,
      systemMetrics,
    };

    this.ws.send(JSON.stringify(report));
  }

  public getStatus() {
    return {
      cameraId: this.config.cameraId,
      supervisor: this.supervisor.getStatus(),
      tally: this.tally.getState(),
      intercom: this.intercom.getStatus(),
    };
  }
}
