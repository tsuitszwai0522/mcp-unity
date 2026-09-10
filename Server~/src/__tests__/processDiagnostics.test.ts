import { jest } from '@jest/globals';
import { readFileSync } from 'fs';
import path from 'path';
import { createShutdownHandler } from '../utils/processDiagnostics.js';

describe('process diagnostics', () => {
  it('reports an unexpected shutdown reason and exits non-zero', async () => {
    const stopUnity = jest.fn(async () => {});
    const closeServer = jest.fn(async () => {});
    const exitProcess = jest.fn<(code: number) => void>();
    const report = jest.fn<(message: string, data?: unknown) => void>();
    const shutdown = createShutdownHandler(
      stopUnity,
      closeServer,
      exitProcess,
      report
    );
    const stdinError = new Error('stdin failed');

    await shutdown({ reason: stdinError, exitCode: 1 });

    expect(report).toHaveBeenCalledWith('Unexpected shutdown requested', stdinError);
    expect(stopUnity).toHaveBeenCalledTimes(1);
    expect(closeServer).toHaveBeenCalledTimes(1);
    expect(exitProcess).toHaveBeenCalledWith(1);
  });

  it('reports cleanup failures and upgrades a normal shutdown to exit code 1', async () => {
    const cleanupError = new Error('close failed');
    const exitProcess = jest.fn<(code: number) => void>();
    const report = jest.fn<(message: string, data?: unknown) => void>();
    const shutdown = createShutdownHandler(
      async () => {},
      async () => { throw cleanupError; },
      exitProcess,
      report
    );

    await shutdown();

    expect(report).toHaveBeenCalledWith('Failure while shutting down', cleanupError);
    expect(exitProcess).toHaveBeenCalledWith(1);
  });

  it('reports an overlapping failure and monotonically upgrades the pending exit code', async () => {
    let releaseStop!: () => void;
    const stopGate = new Promise<void>(resolve => { releaseStop = resolve; });
    const exitProcess = jest.fn<(code: number) => void>();
    const report = jest.fn<(message: string, data?: unknown) => void>();
    const shutdown = createShutdownHandler(
      () => stopGate,
      async () => {},
      exitProcess,
      report
    );
    const lateError = new Error('stdin failed during shutdown');

    const initialShutdown = shutdown();
    await Promise.resolve();
    const overlappingShutdown = shutdown({ reason: lateError, exitCode: 1 });
    releaseStop();
    await Promise.all([initialShutdown, overlappingShutdown]);

    expect(report).toHaveBeenCalledWith('Unexpected shutdown requested', lateError);
    expect(exitProcess).toHaveBeenCalledTimes(1);
    expect(exitProcess).toHaveBeenCalledWith(1);
  });

  it('structurally preserves startup, fatal, stdin, and SDK diagnostic wiring', () => {
    // Source-level regression guard only: this proves the wiring statements
    // were not deleted, not that the side-effectful index module executes them.
    const source = readFileSync(path.resolve(process.cwd(), 'src/index.ts'), 'utf8');

    expect(source).toContain("server.server.onerror = (error) => {");
    expect(source).toContain("writeProcessDiagnostic('MCP SDK server error', error);");
    expect(source).toContain("writeProcessDiagnostic('Failed to start server', error);");
    expect(source).toContain("void shutdown({ reason: error, exitCode: 1 });");
    expect(source).toContain("writeProcessDiagnostic('Uncaught exception', error);");
    expect(source).toContain("writeProcessDiagnostic('Unhandled rejection', reason);");
    expect(source).not.toContain("process.stdin.on('error', shutdown)");
  });

  it('structurally preserves delayed dynamic registration and incomplete-list diagnostics', () => {
    // Source-level regression guard only: runtime behavior is covered in
    // dynamicToolRegistration.test.ts; this checks index.ts integration tokens.
    const source = readFileSync(path.resolve(process.cwd(), 'src/index.ts'), 'utf8');

    expect(source).toContain('installDynamicToolRegistration({');
    expect(source).toContain('change.currentState === ConnectionState.Connected');
    expect(source).toContain('notifyToolListChanged: () => server.sendToolListChanged()');
    expect(source).toContain('the MCP tool list is incomplete until the first successful Unity connection');
  });
});
