import { PublicCtor } from '@foundation';
import { IIpc } from '../IIpc';
import { OperationInfo } from '.';

/* @internal */
export class ServiceInfo implements IIpc.ServiceInfo, IIpc.OperationsInfo {
    constructor(private readonly _class: PublicCtor) {
        this._operationInfos = ServiceInfo.createOperationInfosMap(_class);
    }

    //#region Implementation of IIpc.ServiceInfo

    public get endpoint(): string { return this._endpointOverride ?? this._class.name; }
    public set endpoint(value: string) { this._endpointOverride = value; }
    public get operations(): IIpc.OperationsInfo { return this; }

    //#endregion

    //#region Implementation of IIpc.OperationsInfo

    public get(method: string): IIpc.OperationInfo | undefined {
        return this._operationInfos.get(method);
    }
    public get all(): Iterable<IIpc.OperationInfo> { return this._operationInfos.values(); }

    //#endregion

    private _endpointOverride?: string;
    private readonly _operationInfos: Map<string, IIpc.OperationInfo>;

    private static createOperationInfosMap($class: PublicCtor): Map<string, IIpc.OperationInfo> {
        const pairs = Object
            .getOwnPropertyNames($class.prototype)
            .filter(name => name !== 'constructor')
            .map(methodName => [methodName, new OperationInfo($class, methodName)] as const);

        return new Map<string, IIpc.OperationInfo>(pairs);
    }
}
