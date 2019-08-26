import { PipeError } from './pipe-error';

export class PipeBusyError extends PipeError {
    public static readonly defaultMessage = 'The pipe is currently busy. Retrying could help.';
    constructor(message?: string) { super(message || PipeBusyError.defaultMessage); }
}
