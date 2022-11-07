import { CoreIpcPlatform } from '../../foundation';

import {
    IIpcStandard,
} from '../../../std/core';

import {
    IpcStandard,
} from '../../../std/core/ipc/Ipc';


export interface IIpc<
    TCallbackStore = IIpcStandard.CallbackStore,
    TConfigStore = IIpcStandard.ConfigStore.Writer>
    extends IIpcStandard<TCallbackStore, TConfigStore> {

    pipeExists(shortName: string): Promise<boolean>;
}

/* @internal */
export class OldIpc extends IpcStandard implements IIpc {
    public constructor(
        contract?: IIpcStandard.ContractStore,
        config?: IIpcStandard.ConfigStoreInternal,
        proxy?: IIpcStandard.ProxySource,
        callback?: IIpcStandard.CallbackStoreInternal,
        $class?: IIpcStandard.ClassAnnotations,
        $operation?: IIpcStandard.MethodAnnotations,
    ) {
        super(contract, config, proxy, callback, $class, $operation);
    }

    public pipeExists(shortName: string): Promise<boolean> { return CoreIpcPlatform.current.pipeExists(shortName); }
}
