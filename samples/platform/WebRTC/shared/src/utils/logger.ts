export type LogLevel = 'debug' | 'info' | 'warn' | 'error';

export interface LogEntry {
  timestamp: string;
  level: LogLevel;
  component: string;
  message: string;
  context?: Record<string, unknown>;
  error?: {
    message: string;
    stack?: string;
    name?: string;
  };
}

export class Logger {
  private component: string;
  private minLevel: LogLevel;

  private static levelWeights: Record<LogLevel, number> = {
    debug: 0,
    info: 1,
    warn: 2,
    error: 3,
  };

  constructor(component: string, minLevel: LogLevel = 'info') {
    this.component = component;
    this.minLevel = minLevel;
  }

  private shouldLog(level: LogLevel): boolean {
    return Logger.levelWeights[level] >= Logger.levelWeights[this.minLevel];
  }

  private output(entry: LogEntry): void {
    const json = JSON.stringify(entry);
    if (entry.level === 'error') {
      console.error(json);
    } else if (entry.level === 'warn') {
      console.warn(json);
    } else {
      console.log(json);
    }
  }

  public debug(message: string, context?: Record<string, unknown>): void {
    if (!this.shouldLog('debug')) return;
    this.output({
      timestamp: new Date().toISOString(),
      level: 'debug',
      component: this.component,
      message,
      context,
    });
  }

  public info(message: string, context?: Record<string, unknown>): void {
    if (!this.shouldLog('info')) return;
    this.output({
      timestamp: new Date().toISOString(),
      level: 'info',
      component: this.component,
      message,
      context,
    });
  }

  public warn(message: string, context?: Record<string, unknown>): void {
    if (!this.shouldLog('warn')) return;
    this.output({
      timestamp: new Date().toISOString(),
      level: 'warn',
      component: this.component,
      message,
      context,
    });
  }

  public error(message: string, err?: unknown, context?: Record<string, unknown>): void {
    if (!this.shouldLog('error')) return;
    const errorObj =
      err instanceof Error
        ? { message: err.message, stack: err.stack, name: err.name }
        : err !== undefined
        ? { message: String(err) }
        : undefined;

    this.output({
      timestamp: new Date().toISOString(),
      level: 'error',
      component: this.component,
      message,
      context,
      error: errorObj,
    });
  }
}
