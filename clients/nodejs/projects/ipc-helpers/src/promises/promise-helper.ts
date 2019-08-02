import { CancellationToken } from '../cancellation-token/cancellation-token';
import { PromiseCompletionSource } from './promise-completion-source';
import { IDisposable } from '../disposable/disposable';

export class PromiseHelper {
    public static delay(milliseconds: number, cancellationToken: CancellationToken = CancellationToken.default): Promise<void> {
        if (cancellationToken == null) {
            cancellationToken = CancellationToken.default;
        }
        const pcs = new PromiseCompletionSource<void>();

        let registration: IDisposable | null = null;
        let timeoutHandle: NodeJS.Timer | null = null;

        const complete = (succeeded: boolean) => {
            (registration as any as IDisposable).dispose();
            clearTimeout(timeoutHandle as any);
            if (succeeded) {
                pcs.setResult(undefined);
            } else {
                pcs.setCanceled();
            }
        };

        registration = cancellationToken.register(() => complete(false));
        timeoutHandle = setTimeout(() => complete(true), milliseconds);
        return pcs.promise;
    }
    public static yield(): Promise<void> { return PromiseHelper.delay(0); }

    // tslint:disable-next-line: member-ordering
    public static readonly completedPromise = new Promise<void>(resolve => resolve());

    public static fromResult<T>(result: T) { return new Promise<T>(resolve => resolve(result)); }
    public static fromException<T>(error: Error) { return new Promise<T>((_, reject) => reject(error)); }
    public static fromCanceled<T>() { return new Promise<T>((_, reject) => reject(new Error('Task was canceled'))); }

    public static whenAll(...promises: Array<Promise<any>>): Promise<void> {
        if (promises.length === 0) {
            return PromiseHelper.completedPromise;
        }

        const pcs = new PromiseCompletionSource<void>();
        let remaining = promises.length;

        const decrement = () => {
            if (--remaining === 0) {
                pcs.trySetResult(undefined);
            }
        };

        for (const promise of promises) {
            promise.then(decrement, decrement);
        }

        return pcs.promise;
    }
}
