import {
    Trace,
    InvalidOperationError,
    AggregateError,
    UnknownError,

    TimeSpan,
    CancellationToken,

    IAddress,
    ConnectHelper,
    Socket,
} from '@foundation';

export class ConnectWithHelperPal {

    public static async connectWithHelper(
        connectHelper: ConnectHelper,
        address: IAddress,
        timeout: TimeSpan,
        ct: CancellationToken,
    ) {
        let socket: Socket | undefined;
        const errors = new Array<Error>();
        let tryConnectCalled = false;

        await connectHelper({
            address,
            timeout,
            ct,
            async tryConnect() {
                tryConnectCalled = true;
                if (socket) { return true; }

                try {
                    socket = await address.connectAsync(timeout, ct);
                    return true;
                } catch (error) {
                    errors.push(UnknownError.ensureError(error));
                    return false;
                }
            },
        });

        let error: Error | undefined;
        switch (errors.length) {
            case 0: break;
            case 1: error = errors[0]; break;
            default: error = new AggregateError(undefined, ...errors); break;
        }

        if (socket) {
            if (error) { Trace.log(error); }
            return socket;
        } else {
            if (error) { throw error; }
            if (!tryConnectCalled) { throw new InvalidOperationError(`The specified ConnectHelper didn't call the provided tryConnect function.`); }
            throw new InvalidOperationError();
        }
    }

}
