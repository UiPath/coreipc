import { Ipc as IpcBase, AddressSelectionDelegate } from '../std';

import { NamedPipeAddress, AddressBuilder } from '.';

export class Ipc extends IpcBase<AddressBuilder> {
    constructor() {
        super(AddressBuilder);
    }

    public namedPipe(name: string): AddressSelectionDelegate<AddressBuilder, NamedPipeAddress> {
        return (builder) => builder.isPipe(name);
    }
}

export const ipc: Ipc = new Ipc();
