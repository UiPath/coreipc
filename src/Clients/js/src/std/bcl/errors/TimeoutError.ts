import { CoreIpcError } from '.';

export class TimeoutError extends CoreIpcError {
    constructor(args?: { reportedByServer: boolean }) {
        super('The operation has timed out.');
        this.name = 'TimeoutError';
        this.reportedByServer = args?.reportedByServer ?? false;
    }

    public readonly reportedByServer: boolean;
}
