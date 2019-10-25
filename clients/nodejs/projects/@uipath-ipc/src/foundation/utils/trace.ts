import { IDisposable } from '../disposable';
import { ArgumentNullError } from '../../foundation/errors';

export class Trace {
    private static readonly _listeners = new Array<(errorOrText: Error | string, category?: string) => void>();
    public static addListener(listener: (errorOrText: Error | string, category?: string) => void): IDisposable {
        if (!listener) { throw new ArgumentNullError('listener'); }

        Trace._listeners.push(listener);
        return {
            dispose: () => {
                const index = Trace._listeners.indexOf(listener);
                if (index >= 0) {
                    Trace._listeners.splice(index, 1);
                }
            }
        };
    }

    public static log(error: Error): void;
    public static log(text: string): void;
    public static log(errorOrText: Error | string): void {
        for (const listener of Trace._listeners) {
            try {
                listener(errorOrText);
            } catch (_) {
            }
        }
    }

    private static readonly traceCategory = class implements ITraceCategory {
        constructor(private readonly _category: string) { }

        public log(error: Error): void;
        public log(text: string): void;
        public log(errorOrText: Error | string): void {
            for (const listener of Trace._listeners) {
                try {
                    listener(errorOrText, this._category);
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

const promiseTrace = Trace.category('promise');

// @ts-ignore
// tslint:disable-next-line: only-arrow-functions
Promise.prototype.traceError = function(): void {
    this.then(undefined, (reason: any) => {
        if (reason instanceof Error) {
            promiseTrace.log(reason);
        } else {
            promiseTrace.log(`${reason}`);
        }
    });
};

export interface ITraceCategory {
    log(error: Error): void;
    log(text: string): void;
}
