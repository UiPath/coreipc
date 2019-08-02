import { Func0 } from '../delegates/delegates';
import { ArgumentNullError } from '../exceptions/argument-null-error';

export class Lazy<T> {

    private _isValueCreated = false;
    private _value: T | null = null;

    public get isValueCreated(): boolean { return this._isValueCreated; }
    public get value(): T {
        if (!this._isValueCreated) {
            this._value = this._factory();
            this._isValueCreated = true;
        }
        return this._value as any;
    }

    constructor(private readonly _factory: Func0<T>) {
        if (!_factory) {
            throw new ArgumentNullError('_factory');
        }
    }

}
