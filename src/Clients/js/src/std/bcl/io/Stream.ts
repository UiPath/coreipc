import {
    CancellationToken,
    IDisposable,
} from '..';

/* @internal */
export abstract class Stream implements IDisposable {
    public abstract write(buffer: Buffer, ct: CancellationToken): Promise<void>;
    public abstract read(buffer: Buffer, offset: number, length: number, ct: CancellationToken): Promise<number>;
    public abstract dispose(): void;
}
