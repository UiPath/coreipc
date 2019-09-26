import { IDisposable } from '../disposable';
import { CancellationToken } from '../tasks/cancellation-token';
import { TimeSpan } from '../tasks/timespan';

export type ILogicalSocketFactory = () => ILogicalSocket;

export interface ILogicalSocket extends IDisposable {
    connectAsync(path: string, maybeTimeout: TimeSpan | null, cancellationToken: CancellationToken): Promise<void>;
    writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void>;
    addDataListener(listener: (data: Buffer) => void): IDisposable;
    addEndListener(listener: () => void): IDisposable;
    dispose(): void;
}
