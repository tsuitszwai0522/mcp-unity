import { appendFileSync, mkdirSync, writeSync } from 'fs';
import path from 'path';
import { tmpdir } from 'os';

export enum LogLevel {
  DEBUG = 0,
  INFO = 1,
  WARN = 2,
  ERROR = 3
}

export function resolveLogFilePath(configuredPath: string | undefined = process.env.MCP_UNITY_LOG_FILE): string {
  if (!configuredPath) {
    return path.join(tmpdir(), 'mcp-unity-server.log');
  }

  return path.isAbsolute(configuredPath)
    ? configuredPath
    : path.resolve(tmpdir(), configuredPath);
}

function formatDiagnosticData(data: unknown): string {
  if (data instanceof Error) {
    return data.stack || `${data.name}: ${data.message}`;
  }
  if (typeof data === 'string') {
    return data;
  }

  try {
    return JSON.stringify(data, null, 2);
  } catch {
    return String(data);
  }
}

function appendToConfiguredLogFile(output: string): void {
  const logFilePath = resolveLogFilePath();
  mkdirSync(path.dirname(logFilePath), { recursive: true });
  appendFileSync(logFilePath, output);
}

/** Write process-level diagnostics independently of optional logger settings. */
export function writeProcessDiagnostic(
  message: string,
  data?: unknown,
  writeToStderr: (output: string) => void = output => writeSync(process.stderr.fd, output)
): void {
  const timestamp = new Date().toISOString();
  const suffix = data === undefined ? '' : `\n${formatDiagnosticData(data)}`;
  const output = `[${timestamp}] [FATAL] [Process] ${message}${suffix}\n`;
  try {
    writeToStderr(output);
  } catch {
    console.error(output.trimEnd());
  }

  if (process.env.LOGGING_FILE === 'true') {
    try {
      appendToConfiguredLogFile(output);
    } catch (error) {
      console.error('Failed to write process diagnostic to log file:', error);
    }
  }
}

export class Logger {
  private level: LogLevel;
  private prefix: string;
  
  constructor(prefix: string, level: LogLevel = LogLevel.INFO) {
    this.prefix = prefix;
    this.level = level;
  }
  
  debug(message: string, data?: any) {
    this.log(LogLevel.DEBUG, message, data);
  }
  
  info(message: string, data?: any) {
    this.log(LogLevel.INFO, message, data);
  }
  
  warn(message: string, data?: any) {
    this.log(LogLevel.WARN, message, data);
  }
  
  error(message: string, error?: any) {
    this.log(LogLevel.ERROR, message, error);
  }
  
  isLoggingEnabled(): boolean {
    return process.env.LOGGING === 'true';
  }
  
  isLoggingFileEnabled(): boolean {
    return process.env.LOGGING_FILE === 'true';
  }
  
  private log(level: LogLevel, message: string, data?: any) {
    if (level < this.level) return;
    
    const timestamp = new Date().toISOString();
    const levelStr = LogLevel[level];
    const logMessage = `[${timestamp}] [${levelStr}] [${this.prefix}] ${message}`;

    // Write to file if file logging is enabled
    if (this.isLoggingFileEnabled()) {
      try {
          appendToConfiguredLogFile(logMessage + '\n');
          if (data) {
              appendToConfiguredLogFile(JSON.stringify(data, null, 2) + '\n');
          }
      } catch (error) {
          console.error('Failed to write to log file:', error);
      }
    }
    
    // Write to console if logging is enabled
    if (this.isLoggingEnabled()) {
      if (data) {
        console.error(logMessage, data);
      } else {
        console.error(logMessage);
      }
    }
  }
}
