import {
    Ipc as IpcBase,
    AddressBuilder as AddressBuilderBase,
    AddressSelectionDelegate,
    PublicCtor,
} from '../std';
import { NamedPipeAddress } from './NamedPipeAddress';

export class Ipc extends IpcBase<AddressBuilder> {
    constructor() {
        super(AddressBuilder);
    }

    public namedPipe(name: string): AddressSelectionDelegate<AddressBuilder, NamedPipeAddress> {
        return (builder) => builder.isPipe(name);
    }
}

export class AddressBuilder extends AddressBuilderBase {
    public isPipe(name: string): PublicCtor<NamedPipeAddress> {
        super._address = new NamedPipeAddress(name);
        return NamedPipeAddress;
    }
}

export const ipc: Ipc = new Ipc();
