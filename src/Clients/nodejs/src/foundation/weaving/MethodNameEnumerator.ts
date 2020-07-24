import { PublicCtor, argumentIs } from '../../foundation';

/* @internal */
export class MethodNameEnumerator<T = void> {
    public static enumerate<T2>(ctor: PublicCtor<T2>): Array<string & keyof T2> {
        argumentIs(ctor, 'ctor', 'function');

        const instance = new MethodNameEnumerator<T2>(ctor);
        instance.run();
        return instance._output;
    }

    private constructor(private readonly _ctor: PublicCtor<T>) { }

    private _output: Array<string & keyof T> = null as any;

    private run(): void {
        this._output = Reflect
            .ownKeys(this._ctor.prototype)
            .filter(this.refersToANamedMethod)
            ;
    }

    private readonly refersToANamedMethod = (key: string | number | symbol): key is string & keyof T => {
        return typeof key === 'string' &&
            typeof this._ctor.prototype[key] === 'function' &&
            this._ctor !== this._ctor.prototype[key];
    }
}
