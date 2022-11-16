import { Observer } from 'rxjs';
import { CancellationToken, TimeSpan, IAsyncDisposable } from '../../../bcl';
import { ConnectHelper, Address } from '../..';
import { IMessageStream } from '..';
import { RpcMessage, RpcCallContext } from '.';

/* @internal */
export interface IRpcChannel extends IAsyncDisposable {
    readonly isDisposed: boolean;
    call(
        request: RpcMessage.Request,
        timeout: TimeSpan,
        ct: CancellationToken
    ): Promise<RpcMessage.Response>;
}

export module IRpcChannel {
    /* @internal */
    export interface Factory<TAddress extends Address> {
        create(
            address: TAddress,
            connectHelper: ConnectHelper<TAddress>,
            connectTimeout: TimeSpan,
            ct: CancellationToken,
            observer: Observer<RpcCallContext.Incomming>,
            messageStreamFactory?: IMessageStream.Factory
        ): IRpcChannel;
    }
}
