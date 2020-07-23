import { PublicCtor } from '../../../foundation';
import { IIpc } from '../IIpc';
import { ServiceInfo } from '.';

/* @internal */
export class ContractStore implements IIpc.ContractStore {
    public getOrAdd($class: PublicCtor): IIpc.ServiceInfo {
        return this._map.get($class) ?? this.add($class);
    }
    public get($class: PublicCtor): IIpc.ServiceInfo | undefined {
        return this._map.get($class);
    }

    private readonly _map = new Map<PublicCtor, IIpc.ServiceInfo>();

    private add($class: PublicCtor): IIpc.ServiceInfo {
        const serviceInfo = new ServiceInfo($class);
        this._map.set($class, serviceInfo);
        return serviceInfo;
    }
}
