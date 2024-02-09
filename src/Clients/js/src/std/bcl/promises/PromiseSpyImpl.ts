import { OperationCanceledError } from '../errors';
import { PromiseSpy } from './PromiseSpy';
import { PromiseStatus } from './PromiseStatus';

/* @internal */
export class PromiseSpyImpl<T> implements PromiseSpy<T> {
    constructor(public readonly promise: Promise<T>) {
        promise.then(this.resolveHandler, this.rejectHandler);
    }

    public status: PromiseStatus = PromiseStatus.Running;
    public result: T | undefined;
    public error: Error | undefined;

    private readonly resolveHandler = (result: T): void => {
        this.status = PromiseStatus.Succeeded;
        this.result = result;
    };
    private readonly rejectHandler = (error: Error): void => {
        if (error instanceof OperationCanceledError) {
            this.status = PromiseStatus.Canceled;
        } else {
            this.status = PromiseStatus.Faulted;
        }
        this.error = error;
    };
}
