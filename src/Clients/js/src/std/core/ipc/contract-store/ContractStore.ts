import { PublicCtor, NamedPublicCtor } from '@foundation';
import { IIpcStandard } from '../IIpc';
import { ServiceInfo } from '.';

/* @internal */
export class ContractStore implements IIpcStandard.ContractStore {
    public getOrAdd($class: NamedPublicCtor): IIpcStandard.ServiceInfo {
        return this._map.get($class) ?? this.add($class);
    }
    public get($class: PublicCtor): IIpcStandard.ServiceInfo | undefined {
        return this._map.get($class);
    }

    private readonly _map = new Map<PublicCtor, IIpcStandard.ServiceInfo>();

    private add($class: NamedPublicCtor): IIpcStandard.ServiceInfo {
        const serviceInfo = new ServiceInfo($class);
        this._map.set($class, serviceInfo);
        return serviceInfo;
    }
}
