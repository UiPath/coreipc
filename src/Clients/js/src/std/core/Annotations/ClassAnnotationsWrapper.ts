// tslint:disable: no-namespace
import { PublicCtor } from '../..';
import { IServiceProvider } from '..';
import { ClassAnnotations } from '.';

/* @internal */
export class ClassAnnotationsWrapper {
    public constructor(private readonly _domain: IServiceProvider) {}

    public readonly iface: ClassAnnotations = (<TService>(
        arg0: PublicCtor<TService> | { endpoint?: string },
    ): void | ((ctor: PublicCtor) => void) => {
        if (typeof arg0 === 'function') {
            const _ = this._domain.contractStore.getOrCreate(arg0);
            return;
        }

        if (arg0 instanceof Object) {
            return (ctor: PublicCtor): void => {
                const serviceDescriptor = this._domain.contractStore.getOrCreate(ctor);

                if (arg0.endpoint) {
                    serviceDescriptor.endpoint = arg0.endpoint;
                }
            };
        }

        return;
    }) as any;
}
