import { writeProcessDiagnostic } from './logger.js';

export interface ShutdownRequest {
  reason?: unknown;
  exitCode?: number;
}

type ShutdownStep = () => Promise<void>;
type ExitProcess = (code: number) => void;
type ReportDiagnostic = (message: string, data?: unknown) => void;

/**
 * Build a single-use shutdown handler whose failure exit status remains observable.
 */
export function createShutdownHandler(
  stopUnity: ShutdownStep,
  closeServer: ShutdownStep,
  exitProcess: ExitProcess = code => process.exit(code),
  reportDiagnostic: ReportDiagnostic = writeProcessDiagnostic
): (request?: ShutdownRequest) => Promise<void> {
  let requestedExitCode = 0;
  let shutdownPromise: Promise<void> | null = null;

  return (request: ShutdownRequest = {}): Promise<void> => {
    if (request.reason !== undefined) {
      reportDiagnostic('Unexpected shutdown requested', request.reason);
    }
    if ((request.exitCode ?? 0) > requestedExitCode) {
      requestedExitCode = request.exitCode ?? 0;
    }

    if (!shutdownPromise) {
      // Defer cleanup by one microtask so shutdownPromise is assigned before
      // either cleanup step can trigger a re-entrant shutdown request.
      shutdownPromise = Promise.resolve().then(async () => {
        try {
          await stopUnity();
          await closeServer();
        } catch (error) {
          requestedExitCode = 1;
          reportDiagnostic('Failure while shutting down', error);
        }

        exitProcess(requestedExitCode);
      });
    }

    return shutdownPromise;
  };
}
