import { Observer } from 'rxjs';
import { CancellationToken, TimeSpan } from '../../../bcl';
import { Address, ConnectHelper } from '../..';
import { IMessageStream } from '..';
import { IRpcChannel, RpcCallContext } from '.';

/* @internal */
export interface IRpcChannelFactory {
    create(
        address: Address,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        observer: Observer<RpcCallContext.Incomming>,
        messageStreamFactory?: IMessageStream.Factory,
    ): IRpcChannel;
}
