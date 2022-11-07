import { PublicCtor, NamedPublicCtor, IAddress } from '@foundation';
import { IIpcStandard } from '..';
import { IpcStandard } from '../Ipc';

/* @internal */
export class ProxySource implements IIpcStandard.ProxySource {
    public constructor(private readonly _owner: IpcStandard) { }

    public get<TService, TAddress extends IAddress = IAddress>(address: TAddress, service: NamedPublicCtor<TService>): TService {
        const proxyManager = this._owner
            .pipeManagerRegistry.get(address)
            .proxyManagerRegistry.get(service);

        return proxyManager.proxy;
    }
}
