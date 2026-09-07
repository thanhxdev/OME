import { ComponentHealth } from '../types/metrics';

export interface HealthCheckResult {
  status: 'healthy' | 'degraded' | 'unhealthy';
  uptime: number; // in seconds
  timestamp: string;
  version: string;
  components: Record<string, Partial<ComponentHealth>>;
}

export class HealthCheckManager {
  private startTime: number;
  private version: string;
  private components: Map<string, Partial<ComponentHealth>>;

  constructor(version: string = '1.0.0') {
    this.startTime = Date.now();
    this.version = version;
    this.components = new Map();
  }

  public registerComponent(name: string, initialStatus: 'healthy' | 'degraded' | 'unhealthy' = 'healthy'): void {
    this.components.set(name, {
      status: initialStatus,
      timestamp: Date.now(),
    });
  }

  public updateComponentStatus(name: string, status: 'healthy' | 'degraded' | 'unhealthy', message?: string): void {
    this.components.set(name, {
      status,
      timestamp: Date.now(),
      message,
    });
  }

  public getHealth(): HealthCheckResult {
    const uptime = Math.floor((Date.now() - this.startTime) / 1000);
    let overallStatus: 'healthy' | 'degraded' | 'unhealthy' = 'healthy';

    const componentsObj: Record<string, Partial<ComponentHealth>> = {};

    for (const [name, data] of this.components.entries()) {
      componentsObj[name] = data;
      if (data.status === 'unhealthy') {
        overallStatus = 'unhealthy';
      } else if (data.status === 'degraded' && overallStatus !== 'unhealthy') {
        overallStatus = 'degraded';
      }
    }

    return {
      status: overallStatus,
      uptime,
      timestamp: new Date().toISOString(),
      version: this.version,
      components: componentsObj,
    };
  }
}
