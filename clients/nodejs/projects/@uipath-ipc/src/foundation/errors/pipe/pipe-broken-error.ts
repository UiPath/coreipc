import { PipeError } from './pipe-error';

export class PipeBrokenError extends PipeError {
    public static readonly defaultMessage = 'Broken pipe.';
    constructor(message?: string) { super(message || PipeBrokenError.defaultMessage); }
}
