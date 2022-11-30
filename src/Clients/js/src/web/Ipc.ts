import { IpcBaseImpl, AddressSelectionDelegate, IpcBase } from '../std';
import { BrowserWebSocketAddress } from './Transport';
import { WebAddressBuilder } from './WebAddressBuilder';

/* @internal */
export class IpcWebImpl extends IpcBaseImpl<WebAddressBuilder> implements Ipc {
    constructor() {
        super(WebAddressBuilder);
    }

    public webSocket(
        url: string,
    ): AddressSelectionDelegate<WebAddressBuilder, BrowserWebSocketAddress> {
        return builder => builder.isWebSocket(url);
    }
}

export interface Ipc extends IpcBase<WebAddressBuilder> {
    webSocket(url: string): AddressSelectionDelegate<WebAddressBuilder, BrowserWebSocketAddress>;
}

export const ipc: Ipc = new IpcWebImpl();
