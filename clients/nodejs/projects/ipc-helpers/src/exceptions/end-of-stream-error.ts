export class EndOfStreamError extends Error {
    // istanbul ignore next
    constructor(public readonly inner: Error | null = null) {
        super('Attempted to read past the end of the stream.');
    }
}
