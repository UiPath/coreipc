import { IDisposable } from '../disposable/disposable';
import { TimeSpan } from './timespan';
import { ArgumentNullError } from '../errors/argument-null-error';
import { ArgumentError } from '../errors/argument-error';

export class Timeout implements IDisposable {
    public static maybeCreate(maybeTimeSpan: TimeSpan | null, callback: () => void): IDisposable {
        if (!callback) { throw new ArgumentNullError('callback'); }
        if (maybeTimeSpan) {
            return new Timeout(maybeTimeSpan, callback);
        } else {
            return { dispose: () => { /* */ } };
        }
    }

    private _mayNotClear = false;
    private readonly _id: NodeJS.Timeout;

    constructor(timespan: TimeSpan, private readonly _callback: () => void) {
        if (!timespan) { throw new ArgumentNullError('timespan'); }
        if (!_callback) { throw new ArgumentNullError('_callback'); }
        if (timespan.isNegative) { throw new ArgumentError('The specified TimeSpan cannot be negative.', 'timespan'); }
        this._id = setTimeout(this.callback.bind(this), timespan.totalMilliseconds);
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
