import { CoreIpcError } from '.';

export class EndOfStreamError extends CoreIpcError {
    constructor() { super('Attempted to read past the end of the stream.'); }
}
