import { IpcBaseImpl, AddressSelectionDelegate, IpcBase } from '../std';
import { NodeAddressBuilder } from './NodeAddressBuilder';

/* @internal */
export class IpcNodeImpl extends IpcBaseImpl<NodeAddressBuilder> implements Ipc {
    constructor() {
        super(NodeAddressBuilder);
    }

    public namedPipe(name: string): AddressSelectionDelegate<NodeAddressBuilder> {
        return builder => builder.isPipe(name);
    }
}

export interface Ipc extends IpcBase<NodeAddressBuilder> {
    namedPipe(name: string): AddressSelectionDelegate<NodeAddressBuilder>;
}

export const ipc: Ipc = new IpcNodeImpl();

