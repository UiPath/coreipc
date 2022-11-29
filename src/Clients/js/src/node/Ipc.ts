import { IpcBaseImpl, AddressSelectionDelegate, IpcBase } from '../std';
import { NodeAddressBuilder } from './NodeAddressBuilder';
import { NamedPipeAddress } from '.';

class IpcNodeImpl extends IpcBaseImpl<NodeAddressBuilder> implements Ipc {
    constructor() {
        super(NodeAddressBuilder);
    }

    public namedPipe(
        name: string,
    ): AddressSelectionDelegate<NodeAddressBuilder, NamedPipeAddress> {
        return builder => builder.isPipe(name);
    }
}

export interface Ipc extends IpcBase<NodeAddressBuilder> {
    namedPipe(
        name: string,
    ): AddressSelectionDelegate<NodeAddressBuilder, NamedPipeAddress>;
}

export const ipc: Ipc = new IpcNodeImpl();
