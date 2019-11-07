import { InvalidOperationError } from '../errors/invalid-operation-error';

export class Quack<T> {
    private _items = new Array<T>();

    public get length(): number { return this._items.length; }
    public get any(): boolean { return this._items.length > 0; }
    public get empty(): boolean { return this._items.length === 0; }

    public enqueue(item: T): void {
        this._items.splice(0, 0, item);
    }
    public push(item: T): void {
        this._items.push(item);
    }
    public pop(): T {
        if (this.empty) { throw new InvalidOperationError('Quack is empty.'); }
        return this._items.pop() as T;
    }
}
