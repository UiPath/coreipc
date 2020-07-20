import { PublicCtor } from '@foundation';
import { IIpc } from '..';
import { Ipc } from '../Ipc';

/* @internal */
export class ProxySource implements IIpc.ProxySource {
    public constructor(private readonly _owner: Ipc) { }

    public get<TService, TPipeName extends string = string>(pipeName: TPipeName, service: PublicCtor<TService>): TService {
        const proxyManager = this._owner
            .pipeManagerRegistry.get(pipeName)
            .proxyManagerRegistry.get(service);

        return proxyManager.proxy;
    }
}
