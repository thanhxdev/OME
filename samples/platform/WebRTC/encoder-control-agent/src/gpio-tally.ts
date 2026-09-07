import { TallyState } from '@webrtc-broadcast/shared';

export class GpioTallyController {
  private currentState: TallyState = 'off';

  constructor(
    private onAirPin: number,
    private previewPin: number,
    private mockMode = true
  ) {}

  public setTally(state: TallyState): void {
    this.currentState = state;

    if (this.mockMode) {
      // In mock/development mode, simulate GPIO pin high/low states
      const onAirLevel = state === 'on-air' ? 1 : 0;
      const previewLevel = state === 'preview' ? 1 : 0;
      // State updated
    } else {
      // In physical hardware mode, write to /sys/class/gpio or native addon
      try {
        const fs = require('fs');
        fs.writeFileSync(`/sys/class/gpio/gpio${this.onAirPin}/value`, state === 'on-air' ? '1' : '0');
        fs.writeFileSync(`/sys/class/gpio/gpio${this.previewPin}/value`, state === 'preview' ? '1' : '0');
      } catch {
        // Fallback to mock mode if sysfs not writable
      }
    }
  }

  public getState(): TallyState {
    return this.currentState;
  }
}
