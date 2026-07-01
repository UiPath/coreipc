import {
    AbortSignalAdapter,
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
    public static create<TService>(params: {
        sp: IServiceProvider;
        service: PublicCtor<TService>;
        address: Address;
        methodName: keyof TService & string;
        args: unknown[];
    }): [
        RpcMessage.Request,
        PublicCtor | Primitive | undefined,
        CancellationToken,
        TimeSpan,
    ] {
        const maybeServiceContract = params.sp.contractStore.maybeGet(
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
        // Copy so an AbortSignal can be substituted in-place (below).
        const args = [...params.args];

        for (let i = 0; i < args.length; i++) {
            const arg = args[i];
            if (arg instanceof Message) {
                message = arg;
                continue;
            }
            // Accept a CancellationToken OR (bridged) an AbortSignal wherever a
            // cancellation argument is expected. Capture the live token for
            // cancellation (local binding + the CancellationRequest frame), but
            // put an INERT placeholder in the wire slot: the cancellation signal
            // is out-of-band, and the receiver ignores the slot's content for a
            // CancellationToken parameter. A live CancellationTokenSource token is
            // a circular object graph (token -> source -> token) that JSON cannot
            // serialize, so the raw token must never reach Converter.
            const token = AbortSignalAdapter.ensureCancellationToken(arg);
            if (token) {
                ct = token;
                args[i] = CancellationToken.none;
            }
        }

        ct = ct ?? CancellationToken.none;

        const timeout =
            message?.RequestTimeout ??
            params.sp.configStore.getRequestTimeout(
                params.address,
                params.service,
            ) ??
            Timeout.infiniteTimeSpan;

        if (
            hasEndingCt &&
            (args.length === 0 ||
                !(args[args.length - 1] instanceof CancellationToken))
        ) {
            args.push(CancellationToken.none);
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
