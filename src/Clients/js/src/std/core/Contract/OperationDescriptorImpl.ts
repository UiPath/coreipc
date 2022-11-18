import 'reflect-metadata';
import { PublicCtor, Primitive, CancellationToken } from '../..';
import { OperationDescriptor, ServiceDescriptorImpl } from '.';

/* @internal */
export class OperationDescriptorImpl<TService> implements OperationDescriptor {
    constructor(
        private readonly _service: ServiceDescriptorImpl<TService>,
        private readonly _methodName: string,
    ) {}

    get methodName(): string {
        return this._methodName;
    }

    get operationName(): string {
        return this._operationName ?? this.methodName;
    }
    set operationName(value: string) {
        this._operationName = value;
    }

    public get hasEndingCancellationToken(): boolean {
        const parameterTypes = this.parameterTypes;

        return (
            parameterTypes &&
            parameterTypes.length > 0 &&
            parameterTypes[parameterTypes.length - 1] === (CancellationToken as any)
        );
    }
    public get returnType(): PublicCtor {
        return Reflect.getMetadata(
            'design:returntype',
            this._service.$class.prototype,
            this.methodName,
        );
    }
    public get parameterTypes(): readonly PublicCtor[] {
        return Reflect.getMetadata(
            'design:paramtypes',
            this._service.$class.prototype,
            this.methodName,
        );
    }
    public returnsPromiseOf?: PublicCtor<unknown> | Primitive;

    private _operationName: string | undefined;
}
