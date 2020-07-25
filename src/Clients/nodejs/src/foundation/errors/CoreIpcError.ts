export class CoreIpcError extends Error {
    constructor(message?: string) {
        super(message);
        this.name = 'CoreIpcError';
    }
}
