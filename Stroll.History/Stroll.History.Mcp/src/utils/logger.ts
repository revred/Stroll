/**
 * Lightweight logging utility for Stroll History MCP Service
 */

export type LogLevel = 'debug' | 'info' | 'warn' | 'error';

interface Logger {
  debug(message: string, ...args: any[]): void;
  info(message: string, ...args: any[]): void;
  warn(message: string, ...args: any[]): void;
  error(message: string, ...args: any[]): void;
}

export function createLogger(name: string): Logger {
  const logLevel = (process.env.LOG_LEVEL || 'info') as LogLevel;
  const levels = { debug: 0, info: 1, warn: 2, error: 3 };
  const currentLevel = levels[logLevel];

  function log(level: LogLevel, message: string, ...args: any[]): void {
    if (levels[level] < currentLevel) return;

    const timestamp = new Date().toISOString();
    const levelStr = level.toUpperCase().padEnd(5);
    console.error(`${timestamp} [${levelStr}] [${name}] ${message}`, ...args);
  }

  return {
    debug: (message: string, ...args: any[]) => log('debug', message, ...args),
    info: (message: string, ...args: any[]) => log('info', message, ...args),
    warn: (message: string, ...args: any[]) => log('warn', message, ...args),
    error: (message: string, ...args: any[]) => log('error', message, ...args),
  };
}