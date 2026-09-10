import { jest } from '@jest/globals';
import { mkdtempSync, readFileSync, rmSync } from 'fs';
import path from 'path';
import { tmpdir } from 'os';
import {
  Logger,
  LogLevel,
  resolveLogFilePath,
  writeProcessDiagnostic
} from '../utils/logger.js';

describe('Logger', () => {
  let consoleSpy: jest.SpiedFunction<typeof console.log>;
  let consoleErrorSpy: jest.SpiedFunction<typeof console.error>;
  let originalLogging: string | undefined;
  let originalLoggingFile: string | undefined;
  let originalLogFile: string | undefined;

  beforeEach(() => {
    originalLogging = process.env.LOGGING;
    originalLoggingFile = process.env.LOGGING_FILE;
    originalLogFile = process.env.MCP_UNITY_LOG_FILE;
    consoleSpy = jest.spyOn(console, 'log').mockImplementation(() => {});
    consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    consoleSpy.mockRestore();
    consoleErrorSpy.mockRestore();
    setOrDeleteEnv('LOGGING', originalLogging);
    setOrDeleteEnv('LOGGING_FILE', originalLoggingFile);
    setOrDeleteEnv('MCP_UNITY_LOG_FILE', originalLogFile);
  });

  describe('constructor', () => {
    it('should create logger with prefix and default level', () => {
      const logger = new Logger('TestPrefix');

      expect(logger).toBeInstanceOf(Logger);
    });

    it('should create logger with custom level', () => {
      const logger = new Logger('TestPrefix', LogLevel.DEBUG);

      expect(logger).toBeInstanceOf(Logger);
    });
  });

  describe('log level filtering', () => {
    it('should not log messages below the configured level', () => {
      // Set environment variable for logging
      const originalEnv = process.env.LOGGING;
      process.env.LOGGING = 'true';

      const logger = new Logger('Test', LogLevel.WARN);

      logger.debug('debug message');
      logger.info('info message');

      expect(consoleSpy).not.toHaveBeenCalled();

      process.env.LOGGING = originalEnv;
    });

    it('should respect log level hierarchy', () => {
      // This test verifies the Logger respects log levels
      // Actual console output depends on LOGGING at each call.
      const logger = new Logger('Test', LogLevel.DEBUG);

      // All methods should be callable without throwing
      expect(() => {
        logger.debug('debug');
        logger.info('info');
        logger.warn('warn');
        logger.error('error');
      }).not.toThrow();
    });

    it('writes enabled console logs to stderr without touching stdout', () => {
      process.env.LOGGING = 'true';
      const logger = new Logger('Test', LogLevel.INFO);

      logger.info('transport-safe log');

      expect(consoleErrorSpy).toHaveBeenCalledWith(
        expect.stringContaining('transport-safe log')
      );
      expect(consoleSpy).not.toHaveBeenCalled();
    });
  });

  describe('isLoggingEnabled', () => {
    it('should return a boolean value', () => {
      const logger = new Logger('Test');
      expect(typeof logger.isLoggingEnabled()).toBe('boolean');
    });
  });

  describe('isLoggingFileEnabled', () => {
    it('should return a boolean value', () => {
      const logger = new Logger('Test');
      expect(typeof logger.isLoggingFileEnabled()).toBe('boolean');
    });
  });

  describe('file path resolution', () => {
    it('uses an absolute OS-temp path instead of the process cwd', () => {
      const resolved = resolveLogFilePath();

      expect(path.isAbsolute(resolved)).toBe(true);
      expect(resolved).toBe(path.join(tmpdir(), 'mcp-unity-server.log'));
      expect(resolved).not.toBe(path.resolve(process.cwd(), 'log.txt'));
    });

    it('resolves relative configured paths against OS temp', () => {
      expect(resolveLogFilePath('diagnostics/server.log')).toBe(
        path.resolve(tmpdir(), 'diagnostics/server.log')
      );
    });

    it('writes file logs to the configured absolute path instead of cwd', () => {
      const temporaryDirectory = mkdtempSync(path.join(tmpdir(), 'mcp-unity-logger-'));
      const originalCwd = process.cwd();
      const configuredPath = path.join(temporaryDirectory, 'configured.log');

      try {
        process.chdir(temporaryDirectory);
        process.env.LOGGING_FILE = 'true';
        process.env.MCP_UNITY_LOG_FILE = configuredPath;

        new Logger('Test', LogLevel.ERROR).error('absolute file target');

        expect(readFileSync(configuredPath, 'utf8')).toContain('absolute file target');
      } finally {
        process.chdir(originalCwd);
        rmSync(temporaryDirectory, { recursive: true, force: true });
      }
    });

    it('creates parent directories for a configured nested log path', () => {
      const temporaryDirectory = mkdtempSync(path.join(tmpdir(), 'mcp-unity-logger-'));
      const configuredPath = path.join(temporaryDirectory, 'nested', 'diagnostics', 'server.log');

      try {
        process.env.LOGGING_FILE = 'true';
        process.env.MCP_UNITY_LOG_FILE = configuredPath;

        new Logger('Test', LogLevel.ERROR).error('nested file target');

        expect(readFileSync(configuredPath, 'utf8')).toContain('nested file target');
      } finally {
        rmSync(temporaryDirectory, { recursive: true, force: true });
      }
    });
  });

  describe('process diagnostics', () => {
    it('writes fatal diagnostics to stderr even when optional logging is disabled', () => {
      process.env.LOGGING = 'false';
      process.env.LOGGING_FILE = 'false';
      const writeToStderr = jest.fn<(output: string) => void>();

      writeProcessDiagnostic('startup failed', new Error('boom'), writeToStderr);

      expect(writeToStderr).toHaveBeenCalledWith(expect.stringContaining('startup failed'));
      expect(writeToStderr).toHaveBeenCalledWith(expect.stringContaining('Error: boom'));
      expect(consoleSpy).not.toHaveBeenCalled();
    });

    it('structurally preserves the synchronous stderr descriptor selection', () => {
      // Source-level regression guard only: this proves the descriptor choice
      // was not deleted, not that a fatal process path executes it.
      const source = readFileSync(
        path.resolve(process.cwd(), 'src/utils/logger.ts'),
        'utf8'
      );

      expect(source).toContain('writeSync(process.stderr.fd, output)');
      expect(source).not.toContain('writeSync(process.stdout.fd, output)');
    });

    it('also persists fatal diagnostics when file logging is enabled', () => {
      const temporaryDirectory = mkdtempSync(path.join(tmpdir(), 'mcp-unity-fatal-'));
      const configuredPath = path.join(temporaryDirectory, 'fatal.log');
      const writeToStderr = jest.fn<(output: string) => void>();

      try {
        process.env.LOGGING_FILE = 'true';
        process.env.MCP_UNITY_LOG_FILE = configuredPath;

        writeProcessDiagnostic('fatal event', new Error('persist me'), writeToStderr);

        const contents = readFileSync(configuredPath, 'utf8');
        expect(contents).toContain('fatal event');
        expect(contents).toContain('Error: persist me');
        expect(writeToStderr).toHaveBeenCalled();
      } finally {
        rmSync(temporaryDirectory, { recursive: true, force: true });
      }
    });
  });

  describe('logging methods', () => {
    it('should have debug method', () => {
      const logger = new Logger('Test');
      expect(typeof logger.debug).toBe('function');
    });

    it('should have info method', () => {
      const logger = new Logger('Test');
      expect(typeof logger.info).toBe('function');
    });

    it('should have warn method', () => {
      const logger = new Logger('Test');
      expect(typeof logger.warn).toBe('function');
    });

    it('should have error method', () => {
      const logger = new Logger('Test');
      expect(typeof logger.error).toBe('function');
    });
  });
});

function setOrDeleteEnv(name: string, value: string | undefined): void {
  if (value === undefined) {
    delete process.env[name];
  } else {
    process.env[name] = value;
  }
}

describe('LogLevel', () => {
  it('should have correct numeric values for ordering', () => {
    expect(LogLevel.DEBUG).toBe(0);
    expect(LogLevel.INFO).toBe(1);
    expect(LogLevel.WARN).toBe(2);
    expect(LogLevel.ERROR).toBe(3);
  });

  it('should allow level comparison', () => {
    expect(LogLevel.DEBUG < LogLevel.INFO).toBe(true);
    expect(LogLevel.INFO < LogLevel.WARN).toBe(true);
    expect(LogLevel.WARN < LogLevel.ERROR).toBe(true);
  });
});
