import { spawn, ChildProcess } from 'child_process';
import { EventEmitter } from 'events';
import { AgentConfig } from './config';

export class ProcessSupervisor extends EventEmitter {
  private child: ChildProcess | null = null;
  private isSupervising = false;
  private restartCount = 0;
  private maxRestarts = 15;
  private restartTimer: NodeJS.Timeout | null = null;
  private currentConfig: AgentConfig;

  constructor(initialConfig: AgentConfig) {
    super();
    this.currentConfig = { ...initialConfig };
  }

  public start(): void {
    if (this.isSupervising) return;
    this.isSupervising = true;
    this.spawnEncoder();
  }

  public stop(): void {
    this.isSupervising = false;
    if (this.restartTimer) {
      clearTimeout(this.restartTimer);
      this.restartTimer = null;
    }
    if (this.child) {
      try {
        this.child.kill('SIGTERM');
      } catch {}
      this.child = null;
    }
    this.emit('stopped');
  }

  public async restartWithConfig(newParams: Partial<AgentConfig>): Promise<void> {
    this.currentConfig = { ...this.currentConfig, ...newParams };

    if (this.child) {
      this.child.kill('SIGTERM');
      this.child = null;
    }

    await new Promise((r) => setTimeout(r, 600));
    if (this.isSupervising) {
      this.spawnEncoder();
    }
  }

  private spawnEncoder(): void {
    if (!this.isSupervising) return;

    const args = [
      this.currentConfig.encoderScriptPath,
      '--camera-id',
      this.currentConfig.cameraId,
      '--input',
      'testpattern',
      '--device-index',
      String(this.currentConfig.deviceIndex),
      '--codec',
      this.currentConfig.codec,
      '--bitrate',
      String(this.currentConfig.bitrate),
      '--resolution',
      this.currentConfig.resolution,
      '--fps',
      String(this.currentConfig.fps),
      '--signaling-url',
      this.currentConfig.signalingWsUrl,
      '--sfu-host',
      this.currentConfig.sfuHost,
      '--sfu-video-port',
      String(this.currentConfig.sfuVideoPort),
      '--hw-accel',
      this.currentConfig.hwAccel,
    ];

    try {
      this.child = spawn('node', args, {
        stdio: ['ignore', 'pipe', 'pipe'],
        env: { ...process.env },
      });

      const pid = this.child.pid;
      this.emit('encoder_started', { pid, config: this.currentConfig });

      this.child.on('exit', (code, signal) => {
        this.child = null;
        this.emit('encoder_exited', { code, signal });

        if (this.isSupervising) {
          this.handleWatchdogCrash();
        }
      });

      this.child.on('error', (err) => {
        this.emit('encoder_error', err);
      });
    } catch (err) {
      this.handleWatchdogCrash();
    }
  }

  private handleWatchdogCrash(): void {
    if (!this.isSupervising) return;
    this.restartCount++;

    if (this.restartCount > this.maxRestarts) {
      this.emit('watchdog_max_restarts_exceeded');
      return;
    }

    const backoffMs = Math.min(5000, 1000 * Math.min(this.restartCount, 5));
    this.emit('watchdog_restarting', { attempt: this.restartCount, backoffMs });

    this.restartTimer = setTimeout(() => {
      this.spawnEncoder();
    }, backoffMs);
  }

  public getStatus() {
    return {
      supervising: this.isSupervising,
      pid: this.child ? this.child.pid : null,
      restartCount: this.restartCount,
      config: this.currentConfig,
    };
  }
}
