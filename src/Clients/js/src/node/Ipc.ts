import { IpcBase, AddressSelectionDelegate } from '../std';
import { NodeAddressBuilder } from './NodeAddressBuilder';
import { NamedPipeAddress } from '.';

export class Ipc extends IpcBase<NodeAddressBuilder> {
    constructor() {
        super(NodeAddressBuilder);
    }

    public namedPipe(name: string): AddressSelectionDelegate<NodeAddressBuilder, NamedPipeAddress> {
        return builder => builder.isPipe(name);
    }
}

export const ipc: Ipc = new Ipc();
