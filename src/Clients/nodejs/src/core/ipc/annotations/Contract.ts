// tslint:disable: no-namespace
import { PublicCtor } from '@foundation';
import { IIpc } from '..';
import { Ipc } from '../Ipc';

/* @internal */
export class ClassAnnotationsWrapper {
    public constructor(private readonly _owner: Ipc) {
        (this.$ as any).hasEndpointName = this.hasEndpointName;
    }

    public get contract(): IIpc.ClassAnnotations { return this.$ as any; }

    private readonly $ = ($class: PublicCtor): void => {
        const _ = this._owner.contract.getOrAdd($class);
    }

    private readonly hasEndpointName = (endpointName: string): (ctor: PublicCtor<unknown>) => void => {
        return ($class: PublicCtor): void => {
            this._owner.contract.getOrAdd($class).endpoint = endpointName;
        };
    }
}
