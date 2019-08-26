import { PipeError } from './pipe-error';

export class PipeNotFoundError extends PipeError {
    public static readonly defaultMessage = 'No pipe with that name exists.';
    constructor(message?: string) { super(message || PipeNotFoundError.defaultMessage); }
}
