import { EventEmitter } from 'events';

export class IntercomClient extends EventEmitter {
  private isListening = false;
  private isTransmitting = false;

  constructor(private cameraId: string) {
    super();
  }

  public handleIncomingAudio(from: string, audioData: string): void {
    this.isListening = true;
    this.emit('audio_received', { from, audioData });
  }

  public startTransmission(): string {
    this.isTransmitting = true;
    this.emit('ptt_started');
    return `OPUS_AUDIO_STREAM_${this.cameraId}_${Date.now()}`;
  }

  public stopTransmission(): void {
    this.isTransmitting = false;
    this.emit('ptt_stopped');
  }

  public getStatus() {
    return {
      isListening: this.isListening,
      isTransmitting: this.isTransmitting,
    };
  }
}
