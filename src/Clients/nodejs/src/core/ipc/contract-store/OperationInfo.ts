import 'reflect-metadata';
import { PublicCtor, Primitive, CancellationToken } from '../../../foundation';
import { IIpc } from '../IIpc';

/* @internal */
export class OperationInfo implements IIpc.OperationInfo {
    constructor(
        private readonly _declaringClass: PublicCtor,
        public readonly methodName: string,
    ) { }

    public get operationName(): string { return this._operationName ?? this.methodName; }
    public set operationName(value: string) { this._operationName = value; }

    public get hasEndingCancellationToken(): boolean {
        const parameterTypes = this.parameterTypes;

        return parameterTypes &&
            parameterTypes.length > 0 &&
            parameterTypes[parameterTypes.length - 1] === CancellationToken as any;
    }
    public get returnType(): PublicCtor {
        return Reflect.getMetadata('design:returntype', this._declaringClass.prototype, this.methodName);
    }
    public get parameterTypes(): readonly PublicCtor[] {
        return Reflect.getMetadata('design:paramtypes', this._declaringClass.prototype, this.methodName);
    }
    public returnsPromiseOf?: PublicCtor<unknown> | Primitive;

    private _operationName?: string;
}
