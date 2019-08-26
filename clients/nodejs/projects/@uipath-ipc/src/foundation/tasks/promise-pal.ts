import { CancellationToken } from './cancellation-token';
import { CancellationTokenRegistration } from './cancellation-token-registration';
import { PromiseCompletionSource } from './promise-completion-source';

export class PromisePal {
    public static readonly completedPromise = new Promise<void>(resolve => resolve(undefined));

    public static fromResult<T>(result: T): Promise<T> {
        return new Promise<T>((resolve, _) => resolve(result));
    }
    public static delay(milliseconds: number, cancellationToken: CancellationToken = CancellationToken.none): Promise<void> {
        const pcs = new PromiseCompletionSource<void>();

        const timeoutId = setTimeout(() => {
            pcs.setResult(undefined);
            if (maybeRegistration) {
                maybeRegistration.dispose();
            }
        }, milliseconds);
        const maybeRegistration: CancellationTokenRegistration | null = cancellationToken.canBeCanceled
            ? cancellationToken.register(() => {
                clearTimeout(timeoutId);
                pcs.setCanceled();
            })
            : null;

        return pcs.promise;
    }
}
