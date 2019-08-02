import { InvalidOperationError } from '../exceptions/invalid-operation-error';

export class Quack<T> {
    private readonly _items: T[];

    constructor(...originalItems: T[]) {
        if (originalItems && originalItems.length > 0) {
            this._items = [...originalItems.reverse()];
        } else {
            this._items = [];
        }
    }

    public get count(): number { return this._items.length; }
    public get any(): boolean { return this.count > 0; }
    public get empty(): boolean { return this.count === 0; }

    public enqueue(...items: T[]): void {
        this._items.splice(0, 0, ...items);
    }
    public pushFront(...items: T[]): void {
        this._items.push(...items);
    }
    public tryDequeue(): {
        success: boolean,
        item: T
    } {
        if (this._items.length > 0) {
            return {
                success: true,
                item: this._items.pop() as T
            };
        } else {
            return {
                success: false,
                item: undefined as any as T
            };
        }
    }
    public dequeue(): T {
        const item = this.tryDequeue().item;
        if (item) {
            return item;
        } else {
            throw new InvalidOperationError();
        }
    }
    public tryPeek(): {
        success: boolean,
        item: T
    } {
        if (this._items.length > 0) {
            return {
                success: true,
                item: this._items[this._items.length - 1]
            };
        } else {
            return {
                success: false,
                item: undefined as any as T
            };
        }
    }
    public peek(): T {
        const item = this.tryPeek().item;
        if (item) {
            return item;
        } else {
            throw new InvalidOperationError();
        }
    }
}
