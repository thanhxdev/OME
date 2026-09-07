import { spawn, ChildProcess } from 'child_process';
import { EventEmitter } from 'events';
import { Logger, CodecMode } from '@webrtc-broadcast/shared';
import { DecoderOptions } from '../config';
import { DecoderPipelineBuilder } from './decoder-pipeline-builder';

const logger = new Logger('DecoderRunner');

export class DecoderRunner extends EventEmitter {
  private options: DecoderOptions;
  private process: ChildProcess | null = null;
  private isRunning: boolean = false;
  private currentSdpPath: string = '';

  constructor(options: DecoderOptions) {
    super();
    this.options = { ...options };
  }

  public start(): void {
    if (this.isRunning) return;

    this.isRunning = true;
    this.spawnDecoder();
  }

  private spawnDecoder(): void {
    this.currentSdpPath = DecoderPipelineBuilder.createRtpSdpFile(this.options);
    const args = DecoderPipelineBuilder.buildFfmpegArgs(this.options, this.currentSdpPath);

    logger.info(`Starting Decoder for ${this.options.cameraId} [Codec: ${this.options.activeCodec.toUpperCase()}, Output: ${this.options.output.toUpperCase()}]`);
    logger.debug(`ffmpeg ${args.join(' ')}`);

    try {
      this.process = spawn('ffmpeg', args, { stdio: ['ignore', 'pipe', 'pipe'] });

      this.process.stderr?.on('data', (data: Buffer) => {
        const text = data.toString();
        const fpsMatch = text.match(/fps=\s*([\d.]+)/);
        const dropMatch = text.match(/drop=\s*(\d+)/);
        if (fpsMatch) {
          this.emit('telemetry', {
            fps: parseFloat(fpsMatch[1]),
            droppedFrames: dropMatch ? parseInt(dropMatch[1], 10) : 0,
          });
        }
      });

      this.process.on('close', (code) => {
        logger.warn(`Decoder child process exited with code ${code}`);
        this.process = null;
      });

      this.process.on('error', (err) => {
        logger.error('Decoder process error', err);
      });
    } catch (err) {
      logger.error('Failed to spawn decoder process', err);
    }
  }

  /**
   * Switches decoder codec dynamically when signaled by the encoder/signaling server.
   */
  public switchCodec(codec: CodecMode): void {
    if (this.options.activeCodec === codec) return;

    logger.info(`Switching decoder pipeline codec: ${this.options.activeCodec} -> ${codec}`);
    this.options.activeCodec = codec;

    if (this.process) {
      this.process.kill('SIGTERM');
      setTimeout(() => this.spawnDecoder(), 300);
    } else {
      this.spawnDecoder();
    }
  }

  public stop(): void {
    this.isRunning = false;
    if (this.process) {
      this.process.kill('SIGKILL');
      this.process = null;
    }
    logger.info(`Decoder for ${this.options.cameraId} stopped.`);
  }

  public getOptions(): DecoderOptions {
    return { ...this.options };
  }
}
