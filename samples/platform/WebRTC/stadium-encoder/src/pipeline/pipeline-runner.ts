import { spawn, ChildProcess } from 'child_process';
import { EventEmitter } from 'events';
import { Logger, CodecMode } from '@webrtc-broadcast/shared';
import { EncoderOptions } from '../config';
import { PipelineBuilder } from './pipeline-builder';

const logger = new Logger('PipelineRunner');

export class PipelineRunner extends EventEmitter {
  private options: EncoderOptions;
  private process: ChildProcess | null = null;
  private isRunning: boolean = false;
  private restartTimeout: NodeJS.Timeout | null = null;

  constructor(options: EncoderOptions) {
    super();
    this.options = { ...options };
  }

  public start(): void {
    if (this.isRunning) return;

    this.isRunning = true;
    this.spawnPipeline();
  }

  private spawnPipeline(): void {
    const args = PipelineBuilder.buildFfmpegArgs(this.options);
    logger.info(`Starting pipeline for ${this.options.cameraId} [Codec: ${this.options.codec.toUpperCase()}]`);
    logger.debug(`ffmpeg ${args.join(' ')}`);

    try {
      this.process = spawn('ffmpeg', args, { stdio: ['ignore', 'pipe', 'pipe'] });

      this.process.stderr?.on('data', (data: Buffer) => {
        const text = data.toString();
        // Parse ffmpeg telemetry lines: frame= 120 fps= 60 q=28.0 size= 1200kB time=00:00:02.00 bitrate= 9800.0kbits/s
        this.parseTelemetry(text);
      });

      this.process.on('close', (code) => {
        logger.warn(`Pipeline child process exited with code ${code}`);
        this.process = null;
        if (this.isRunning) {
          logger.info('Auto-restarting media pipeline in 2s...');
          this.restartTimeout = setTimeout(() => this.spawnPipeline(), 2000);
        }
      });

      this.process.on('error', (err) => {
        logger.error('Failed to spawn ffmpeg pipeline', err);
        // Fallback to CPU if hardware acceleration fails
        if (this.options.hwAccel !== 'cpu') {
          logger.warn('Falling back from NVENC/QSV to CPU software encoding...');
          this.options.hwAccel = 'cpu';
          this.spawnPipeline();
        }
      });
    } catch (err) {
      logger.error('Pipeline spawn error', err);
    }
  }

  /**
   * Runtime Codec Switching: Seamlessly switches codec without restarting the main application.
   */
  public switchCodec(codec: CodecMode, bitrate?: number, resolution?: string, fps?: number): void {
    logger.info(`Switching pipeline codec: ${this.options.codec} -> ${codec} (${bitrate || this.options.bitrate} kbps)`);

    this.options.codec = codec;
    if (bitrate) this.options.bitrate = bitrate;
    if (resolution) this.options.resolution = resolution;
    if (fps) this.options.fps = fps;

    if (this.process) {
      // Gracefully terminate current pipeline; the 'close' event will respawn with new codec
      this.process.kill('SIGTERM');
    } else {
      this.spawnPipeline();
    }
  }

  public setBitrate(bitrate: number): void {
    if (this.options.codec === 'throughpass') {
      logger.warn('Cannot dynamically adjust bitrate in Throughpass mode');
      return;
    }
    this.switchCodec(this.options.codec, bitrate);
  }

  private parseTelemetry(text: string): void {
    const fpsMatch = text.match(/fps=\s*([\d.]+)/);
    const bitrateMatch = text.match(/bitrate=\s*([\d.]+)kbits\/s/);
    const dropMatch = text.match(/drop=\s*(\d+)/);

    if (fpsMatch || bitrateMatch) {
      this.emit('telemetry', {
        fps: fpsMatch ? parseFloat(fpsMatch[1]) : this.options.fps,
        bitrate: bitrateMatch ? parseFloat(bitrateMatch[1]) : this.options.bitrate,
        droppedFrames: dropMatch ? parseInt(dropMatch[1], 10) : 0,
      });
    }
  }

  public stop(): void {
    this.isRunning = false;
    if (this.restartTimeout) clearTimeout(this.restartTimeout);
    if (this.process) {
      this.process.kill('SIGKILL');
      this.process = null;
    }
    logger.info(`Pipeline for ${this.options.cameraId} stopped.`);
  }

  public getOptions(): EncoderOptions {
    return { ...this.options };
  }
}
