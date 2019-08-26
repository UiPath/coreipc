import { CancellationToken } from './cancellation-token';
import { IDisposable } from '../disposable/disposable';

export class CancellationTokenSource implements IDisposable {
    public readonly token: CancellationToken = new CancellationToken();
    private readonly _timeoutIds = new Array<NodeJS.Timeout>();

    public cancel(throwOnFirstError: boolean = false): void {
        this.token.cancel(throwOnFirstError);
    }
    public cancelAfter(milliseconds: number): void {
        this._timeoutIds.push(
            setTimeout(
                this.cancel.bind(this),
                milliseconds
            )
        );
    }

    public dispose(): void {
        for (const timeoutId of this._timeoutIds) {
            clearTimeout(timeoutId);
        }
    }
}
