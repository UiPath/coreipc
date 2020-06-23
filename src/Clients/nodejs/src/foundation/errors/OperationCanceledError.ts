import { CoreIpcError } from '.';

export class OperationCanceledError extends CoreIpcError {
    constructor() {
        super('The operation was canceled.');
    }
}
