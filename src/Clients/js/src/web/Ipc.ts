import { Ipc as IpcBase, AddressSelectionDelegate } from '../std';

import { BrowserWebSocketAddress } from '.';
import { WebAddressBuilder } from './WebAddressBuilder';

export class Ipc extends IpcBase<WebAddressBuilder> {
    constructor() {
        super(WebAddressBuilder);
    }

    public webSocket(
        url: string,
    ): AddressSelectionDelegate<WebAddressBuilder, BrowserWebSocketAddress> {
        return (builder) => builder.isWebSocket(url);
    }
}

export const ipc: Ipc = new Ipc();
