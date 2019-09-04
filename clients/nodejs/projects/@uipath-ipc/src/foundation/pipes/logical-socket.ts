import { IDisposable } from '../disposable/disposable';
import { CancellationToken } from '../tasks/cancellation-token';
import { TimeSpan } from '../tasks/timespan';

/* @internal */
export type ILogicalSocketFactory = () => ILogicalSocket;

/* @internal */
export interface ILogicalSocket extends IDisposable {
    connectAsync(path: string, maybeTimeout: TimeSpan | null, cancellationToken: CancellationToken): Promise<void>;
    writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void>;
    addDataListener(listener: (data: Buffer) => void): IDisposable;
    dispose(): void;
}
