import {
    CancellationToken,
    Primitive,
    PublicCtor,
    Timeout,
    TimeSpan,
} from '../..';
import { Address, Converter, Message, RpcMessage, ServiceId } from '..';
import { IServiceProvider } from '.';

/* @internal */
export class RpcRequestFactory {
    public static create<TService, TAddress extends Address>(params: {
        domain: IServiceProvider;
        service: PublicCtor<TService>;
        address: TAddress;
        methodName: keyof TService & string;
        args: unknown[];
    }): [
        RpcMessage.Request,
        PublicCtor | Primitive | undefined,
        CancellationToken,
        TimeSpan,
    ] {
        const maybeServiceContract = params.domain.contractStore.maybeGet(
            params.service,
        );
        const maybeOperationContract =
            maybeServiceContract?.operations.maybeGet(params.methodName);

        const endpoint = maybeServiceContract?.endpoint ?? params.service.name;
        const serviceId = new ServiceId<TService>(params.service, endpoint);

        const operationName =
            maybeOperationContract?.operationName ?? params.methodName;

        const hasEndingCt =
            maybeOperationContract?.hasEndingCancellationToken ?? false;

        const returnsPromiseOf = maybeOperationContract?.returnsPromiseOf;

        let message: Message | undefined;
        let ct: CancellationToken | undefined;

        for (const arg of params.args) {
            if (arg instanceof Message) {
                message = arg;
            }
            if (arg instanceof CancellationToken) {
                ct = arg;
            }
        }

        ct = ct ?? CancellationToken.none;

        const timeout =
            message?.RequestTimeout ??
            params.domain.configStore.getRequestTimeout(
                params.address,
                params.service,
            ) ??
            Timeout.infiniteTimeSpan;

        let args = params.args;

        if (
            hasEndingCt &&
            (params.args.length === 0 ||
                !(
                    params.args[params.args.length - 1] instanceof
                    CancellationToken
                ))
        ) {
            args = [...args, CancellationToken.none];
        }

        const rpcRequest = Converter.toRpcRequest(
            endpoint,
            operationName,
            args,
            timeout,
        );

        return [rpcRequest, returnsPromiseOf, ct, timeout];
    }
}
