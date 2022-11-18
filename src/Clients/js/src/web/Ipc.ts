import { Ipc as IpcBase, AddressSelectionDelegate } from '../std';

import { BrowserWebSocketAddress } from '.';
import { AddressBuilder } from './AddressBuilder';

export class Ipc extends IpcBase<AddressBuilder> {
    constructor() {
        super(AddressBuilder);
    }

    public webSocket(
        url: string,
    ): AddressSelectionDelegate<AddressBuilder, BrowserWebSocketAddress> {
        return (builder) => builder.isWebSocket(url);
    }
}

export const ipc: Ipc = new Ipc();
