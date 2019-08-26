import { IDisposable } from '../disposable/disposable';

export class Timeout implements IDisposable {
    private _mayNotClear = false;
    private readonly _id: NodeJS.Timeout;

    constructor(milliseconds: number, private readonly _callback: () => void) {
        this._id = setTimeout(this.callback.bind(this), milliseconds);
    }
    private callback(): void {
        this._mayNotClear = true;
        this._callback();
    }

    public dispose(): void {
        if (!this._mayNotClear) {
            this._mayNotClear = true;
            clearTimeout(this._id);
        }
    }
}
