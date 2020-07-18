import { PublicCtor, Primitive, InvalidOperationError } from '@foundation';
import { IIpc } from '..';
import { Ipc } from '../Ipc';

/* @internal */
export class MethodAnnotations implements IIpc.MethodAnnotations {
    public constructor(private readonly _owner: Ipc) {
    }

    public hasName(name: string): (target: any, propertyKey: string, descriptor: PropertyDescriptor) => void {
        return (target: any, propertyKey: string, descriptor: PropertyDescriptor): void => {
            const operationInfo = this._owner.contract
                .getOrAdd(target.constructor)
                .operations
                .get(propertyKey);

            if (!operationInfo) {
                throw new InvalidOperationError();
            }

            operationInfo.operationName = name;
        };
    }

    public returnsPromiseOf(genericArgument?: PublicCtor | Primitive): (target: any, propertyKey: string, descriptor: PropertyDescriptor) => void {
        return (target: any, propertyKey: string, descriptor: PropertyDescriptor): void => {
            const operationInfo = this._owner.contract
                .getOrAdd(target.constructor)
                .operations
                .get(propertyKey);

            if (!operationInfo) {
                throw new InvalidOperationError();
            }

            operationInfo.returnsPromiseOf = genericArgument;
        };
    }
}
