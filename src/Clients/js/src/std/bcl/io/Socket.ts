import { Observable } from 'rxjs';
import { CancellationToken, IDisposable } from '../..';

export abstract class Socket implements IDisposable {
    public abstract get $data(): Observable<Buffer>;
    public abstract write(buffer: Buffer, ct: CancellationToken): Promise<void>;
    public abstract dispose(): void;
}
