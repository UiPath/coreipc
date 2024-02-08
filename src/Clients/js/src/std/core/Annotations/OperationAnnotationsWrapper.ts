import { PublicCtor, Primitive, InvalidOperationError } from '../..';
import { IServiceProvider } from '..';
import { OperationAnnotations } from '.';

/* @internal */
export class OperationAnnotationsWrapper {
    public constructor(private readonly _domain: IServiceProvider) {}

    public readonly iface: OperationAnnotations = ((
        arg0:
            | any
            | { name?: string; returnsPromiseOf?: PublicCtor | Primitive },
        propertyKey?: string,
    ): void | ((target: any, propertyKey: string) => void) => {
        if (typeof propertyKey === 'string') {
            const _ = this._domain.contractStore
                .getOrCreate<any>(arg0.constructor)
                .operations.getOrCreate(propertyKey);
            return;
        }

        // tslint:disable-next-line: no-shadowed-variable
        return (target: any, propertyKey: string): void => {
            const operationInfo = this._domain.contractStore
                .getOrCreate<any>(target.constructor)
                .operations.getOrCreate(propertyKey);

            if (!operationInfo) {
                throw new InvalidOperationError();
            }

            if (typeof arg0.name === 'string') {
                operationInfo.operationName = arg0.name;
            }
            if (arg0.returnsPromiseOf != null) {
                operationInfo.returnsPromiseOf = arg0.returnsPromiseOf;
            }
        };
    }) as any;
}
