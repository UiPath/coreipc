import { PublicCtor } from '../..';

import {
    OperationDescriptorTable,
    ServiceDescriptor,
    OperationDescriptor,
    OperationDescriptorImpl,
} from '.';

type Map<TService> = {
    [methodName in keyof TService & string]: OperationDescriptorImpl<TService>;
};

/* @internal */
export class ServiceDescriptorImpl<TService>
    implements ServiceDescriptor<TService>, OperationDescriptorTable<TService>
{
    constructor(public readonly $class: PublicCtor<TService>) {}

    // #region OperationDescriptorTable

    getOrCreate(method: keyof TService & string): OperationDescriptor {
        return (
            this._operations[method] ??
            (this._operations[method] = new OperationDescriptorImpl(this, method))
        );
    }
    maybeGet(method: keyof TService & string): OperationDescriptor | undefined {
        return this._operations[method];
    }
    get all(): Iterable<OperationDescriptor> {
        return Object.values(this._operations);
    }

    // #endregion OperationDescriptorTable

    // #region ServiceDescriptor

    get endpoint(): string {
        return this._endpointOverride ?? this.$class.name;
    }
    set endpoint(value: string) {
        this._endpointOverride = value;
    }
    get operations(): OperationDescriptorTable<TService> {
        return this;
    }

    // #endregion ServiceDescription

    private _endpointOverride: string | undefined;
    private _operations: Map<TService> = {} as any;
}
