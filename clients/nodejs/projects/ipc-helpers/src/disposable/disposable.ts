import { Action0 } from '../delegates/delegates';

export interface IDisposable {
    dispose(): void;
}

/* @internal */
export class Disposable implements IDisposable {
    public static readonly empty: IDisposable = { dispose: () => { /* */ } };
    public static combine(...disposables: IDisposable[]): IDisposable {
        switch (disposables.length) {
            case 0: return Disposable.empty;
            case 1: return disposables[0];
            default:
                return new Disposable(() => {
                    for (const disposable of disposables) {
                        disposable.dispose();
                    }
                });
        }
    }

    constructor(private readonly _action: Action0) { }

    public dispose(): void {
        this._action();
    }
}
