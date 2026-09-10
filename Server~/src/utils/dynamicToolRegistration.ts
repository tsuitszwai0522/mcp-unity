export interface DynamicToolRegistrationOptions {
  isUnityConnected: () => boolean;
  subscribeToConnected: (listener: () => void) => void;
  registerTools: () => Promise<number>;
  notifyToolListChanged: () => void;
  reportRetryFailure: (error: unknown) => void;
}

export interface DynamicToolRegistrationCoordinator {
  registerNow: () => Promise<number>;
}

/**
 * Register Unity-discovered tools once, either during startup or after the
 * first successful background reconnect.
 */
export function installDynamicToolRegistration(
  options: DynamicToolRegistrationOptions
): DynamicToolRegistrationCoordinator {
  let registered = false;
  let inFlight: Promise<number> | null = null;

  const registerNow = async (): Promise<number> => {
    if (registered || !options.isUnityConnected()) {
      return 0;
    }
    if (inFlight) {
      return inFlight;
    }

    inFlight = (async () => {
      const count = await options.registerTools();
      registered = true;
      if (count > 0) {
        options.notifyToolListChanged();
      }
      return count;
    })();

    try {
      return await inFlight;
    } finally {
      inFlight = null;
    }
  };

  options.subscribeToConnected(() => {
    void registerNow().catch(options.reportRetryFailure);
  });

  return { registerNow };
}
