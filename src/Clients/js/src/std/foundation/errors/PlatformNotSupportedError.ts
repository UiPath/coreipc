export class PlatformNotSupportedError extends Error {
    constructor(message?: string) {
        super(message ?? 'Operation is not supported on this platform.');
    }
}
