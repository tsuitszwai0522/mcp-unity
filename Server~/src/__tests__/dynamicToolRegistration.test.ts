import { jest } from '@jest/globals';
import { installDynamicToolRegistration } from '../utils/dynamicToolRegistration.js';

describe('dynamic tool registration', () => {
  it('registers once on the first later Unity connection and notifies the MCP client', async () => {
    let connected = false;
    let onConnected!: () => void;
    const registerTools = jest.fn(async () => 176);
    const notifyToolListChanged = jest.fn();
    const reportRetryFailure = jest.fn();
    const registration = installDynamicToolRegistration({
      isUnityConnected: () => connected,
      subscribeToConnected: listener => { onConnected = listener; },
      registerTools,
      notifyToolListChanged,
      reportRetryFailure
    });

    await expect(registration.registerNow()).resolves.toBe(0);
    expect(registerTools).not.toHaveBeenCalled();

    connected = true;
    onConnected();
    await Promise.resolve();
    await Promise.resolve();

    expect(registerTools).toHaveBeenCalledTimes(1);
    expect(notifyToolListChanged).toHaveBeenCalledTimes(1);
    onConnected();
    await Promise.resolve();
    expect(registerTools).toHaveBeenCalledTimes(1);
    expect(reportRetryFailure).not.toHaveBeenCalled();
  });

  it('reports a failed later registration and permits a retry', async () => {
    let onConnected!: () => void;
    const failure = new Error('list_tools failed');
    const registerTools = jest
      .fn<() => Promise<number>>()
      .mockRejectedValueOnce(failure)
      .mockResolvedValueOnce(2);
    const notifyToolListChanged = jest.fn();
    const reportRetryFailure = jest.fn();
    const registration = installDynamicToolRegistration({
      isUnityConnected: () => true,
      subscribeToConnected: listener => { onConnected = listener; },
      registerTools,
      notifyToolListChanged,
      reportRetryFailure
    });

    onConnected();
    await new Promise<void>(resolve => setImmediate(resolve));
    expect(reportRetryFailure).toHaveBeenCalledWith(failure);

    await expect(registration.registerNow()).resolves.toBe(2);
    expect(registerTools).toHaveBeenCalledTimes(2);
    expect(notifyToolListChanged).toHaveBeenCalledTimes(1);
  });
});
