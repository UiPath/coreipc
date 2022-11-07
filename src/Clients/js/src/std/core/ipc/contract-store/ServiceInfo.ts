import { PublicCtor, NamedPublicCtor, ArgumentOutOfRangeError } from '@foundation';
import { IIpcStandard } from '../IIpc';
import { OperationInfo } from '.';

/* @internal */
export class ServiceInfo implements IIpcStandard.ServiceInfo, IIpcStandard.OperationsInfo {
    constructor(private readonly _class: NamedPublicCtor) {
        if (!_class.name) {
            throw new ArgumentOutOfRangeError('arg0', 'The function name must cannot be null or undefined.');
        }

        this._operationInfos = ServiceInfo.createOperationInfosMap(_class);
    }

    //#region Implementation of IIpcStandard.ServiceInfo

    public get endpoint(): string { return this._endpointOverride ?? this._class.name; }
    public set endpoint(value: string) { this._endpointOverride = value; }
    public get operations(): IIpcStandard.OperationsInfo { return this; }

    //#endregion

    //#region Implementation of IIpcStandard.OperationsInfo

    public get(method: string): IIpcStandard.OperationInfo | undefined {
        return this._operationInfos.get(method);
    }
    public get all(): Iterable<IIpcStandard.OperationInfo> { return this._operationInfos.values(); }

    //#endregion

    private _endpointOverride?: string;
    private readonly _operationInfos: Map<string, IIpcStandard.OperationInfo>;

    private static createOperationInfosMap($class: PublicCtor): Map<string, IIpcStandard.OperationInfo> {
        const pairs = Object
            .getOwnPropertyNames($class.prototype)
            .filter(name => name !== 'constructor')
            .map(methodName => [methodName, new OperationInfo($class, methodName)] as const);

        return new Map<string, IIpcStandard.OperationInfo>(pairs);
    }
}
