import { CoreIpcError } from '.';

export class PlatformNotSupportedError extends CoreIpcError {
    constructor(message?: string) {
        super(message ?? PlatformNotSupportedError.defaultMessage);
    }

    private static readonly defaultMessage = 'Operation is not supported on this platform.';
}
