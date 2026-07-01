/** Tiny leveled logger with a per-tag prefix, mutable at runtime. */
export type LogLevel = 'debug' | 'info' | 'warn' | 'error' | 'silent';

const ORDER: Record<LogLevel, number> = { debug: 0, info: 1, warn: 2, error: 3, silent: 4 };

let currentLevel: LogLevel = 'info';

export function setLogLevel(level: LogLevel): void {
  currentLevel = level;
}

export interface Logger {
  debug(...args: unknown[]): void;
  info(...args: unknown[]): void;
  warn(...args: unknown[]): void;
  error(...args: unknown[]): void;
}

export function createLogger(tag: string): Logger {
  const at = (level: LogLevel) => ORDER[level] >= ORDER[currentLevel];
  const prefix = `[QVS:${tag}]`;
  return {
    debug: (...a) => at('debug') && console.debug(prefix, ...a),
    info: (...a) => at('info') && console.info(prefix, ...a),
    warn: (...a) => at('warn') && console.warn(prefix, ...a),
    error: (...a) => at('error') && console.error(prefix, ...a),
  };
}
