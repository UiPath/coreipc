import { Observer } from 'rxjs';
import { IAsyncDisposable, CancellationToken, Stream } from '../../../bcl';
import { Network } from '.';

/* @internal */
export interface IMessageStream extends IAsyncDisposable {
    writeMessageAsync(message: Network.Message, ct: CancellationToken): Promise<void>;
}

/* @internal */
export module IMessageStream {
    export interface Factory {
        create(stream: Stream, observer: Observer<Network.Message>): IMessageStream;
    }
}
