// tslint:disable: no-namespace
import { PublicCtor } from '@foundation';
import { IIpcStandard } from '..';
import { IpcStandard } from '../Ipc';

/* @internal */
export class ClassAnnotationsWrapper {
    public constructor(private readonly _owner: IpcStandard) { }

    public readonly iface: IIpcStandard.ClassAnnotations = ((arg0: PublicCtor | { endpoint?: string }): void | ((ctor: PublicCtor) => void) => {
        if (arg0 instanceof Function) {
            this._owner.contract.getOrAdd(arg0);
        } else if (arg0 instanceof Object) {
            return (ctor: PublicCtor): void => {
                const serviceInfo = this._owner.contract.getOrAdd(ctor);
                if (arg0.endpoint) {
                    serviceInfo.endpoint = arg0.endpoint;
                }
            };
        }
    }) as any;
}
