import { PublicCtor } from '@foundation';
import { ProxyManager } from '.';
import { Ipc } from '../ipc';
import { PipeManager } from './PipeManager';

/* @internal */
export class ProxyManagerRegistry {
    constructor(
        private readonly _owner: Ipc,
        private readonly _pipeManager: PipeManager,
    ) { }

    public get<TService = unknown>(service: PublicCtor<TService>): ProxyManager<TService> {
        return this._map.get(service) as ProxyManager<TService> ?? this.add(service);
    }

    private add<TService = unknown>(service: PublicCtor<TService>): ProxyManager<TService> {
        const result = new ProxyManager(this._owner, this._pipeManager, service);
        this._map.set(service, result);
        return result;
    }

    private readonly _map = new Map<PublicCtor, ProxyManager>();
}
