import { PublicCtor, Primitive, InvalidOperationError } from '@foundation';
import { IIpc } from '..';
import { Ipc } from '../Ipc';

/* @internal */
export class MethodAnnotationsWrapper {
    public constructor(private readonly _owner: Ipc) { }

    public readonly iface: IIpc.MethodAnnotations = (
        (arg0: any | { name?: string, returnsPromiseOf?: PublicCtor | Primitive }, propertyKey?: string)
            : void | ((target: any, propertyKey: string) => void) => {

            if (typeof propertyKey === 'string') {
                const _ = this._owner
                    .contract.getOrAdd(arg0.constructor as any)
                    .operations.get(propertyKey);
            } else {
                // tslint:disable-next-line: no-shadowed-variable
                return (target: any, propertyKey: string): void => {
                    const operationInfo = this._owner
                        .contract.getOrAdd(target.constructor)
                        .operations.get(propertyKey);

                    if (!operationInfo) { throw new InvalidOperationError(); }

                    if (typeof arg0.name === 'string') {
                        operationInfo.operationName = arg0.name;
                    }
                    if (arg0.returnsPromiseOf != null) {
                        operationInfo.returnsPromiseOf = arg0.returnsPromiseOf;
                    }
                };
            }

        }
    ) as any;
}
