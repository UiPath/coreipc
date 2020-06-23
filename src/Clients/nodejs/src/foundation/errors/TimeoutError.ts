import { CoreIpcError } from '.';

export class TimeoutError extends CoreIpcError {
    constructor() {
        super('The operation has timed out.');
    }
}
