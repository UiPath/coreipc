import { CancellationToken } from '@foundation';

export type MethodCallHookDelegate = (methodName: string, newConnection: boolean, ct: CancellationToken) => Promise<void>;
