import { PromiseCompletionSource, CancellationToken, IDisposable, ArgumentNullError } from '@uipath/ipc-helpers';
import { IPipeWrapper } from './pipe-wrapper';

/* @internal */
export interface IChannelWriter {
    writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void>;
}

/* @internal */
export class ChannelWriter implements IChannelWriter {
    constructor(private readonly _pipe: IPipeWrapper) {
        if (_pipe == null) {
            throw new ArgumentNullError('_pipe');
        }
    }

    public writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
        const pcs = new PromiseCompletionSource<void>();
        let registration: IDisposable | null = null;
        registration = cancellationToken.register(() => {
            if (registration) {
                registration.dispose();
                registration = null;
            }
            pcs.trySetCanceled();
        });
        this._pipe.write(buffer, () => {
            if (registration) {
                registration.dispose();
                registration = null;
            }
            pcs.trySetResult(undefined);
        });
        return pcs.promise;
    }
}
