import { IDisposable, ArgumentNullError } from '../../foundation';

export class Trace {
    private static readonly _listeners = new Array<(unit: Error | string | object, category?: string) => void>();
    public static addListener(listener: (errorOrText: Error | string | object, category?: string) => void): IDisposable {
        if (!listener) { throw new ArgumentNullError('listener'); }

        Trace._listeners.push(listener);
        return {
            dispose: () => {
                const index = Trace._listeners.indexOf(listener);
                if (index >= 0) {
                    Trace._listeners.splice(index, 1);
                }
            },
        };
    }

    public static log(error: Error): void;
    public static log(text: string): void;
    public static log(obj: object): void;
    public static log(arg0: Error | string | object): void {
        for (const listener of Trace._listeners) {
            try {
                listener(arg0);
            } catch (_) {
            }
        }
    }

    private static readonly traceCategory = class implements ITraceCategory {
        constructor(private readonly _category: string) { }

        public log(error: Error): void;
        public log(text: string): void;
        public log(obj: object): void;
        public log(arg0: Error | string | object): void {
            for (const listener of Trace._listeners) {
                try {
                    listener(arg0, this._category);
                } catch (_) {
                }
            }
        }
    };

    public static category(name: string): ITraceCategory {
        return new Trace.traceCategory(name);
    }
}

export { };

declare global {
    interface Promise<T> {
        traceError(): void;
    }
}

const promiseTrace = Trace.category('promise.traceError');

// @ts-ignore
// tslint:disable-next-line: only-arrow-functions
Promise.prototype.traceError = function (): void {
    this.then(_ => { }, (reason: any) => {
        promiseTrace.log(`\r\n\tpromise: ${this}\r\n\treason: ${reason}`);
    });
};

export interface ITraceCategory {
    log(error: Error): void;
    log(text: string): void;
    log(obj: object): void;
}
