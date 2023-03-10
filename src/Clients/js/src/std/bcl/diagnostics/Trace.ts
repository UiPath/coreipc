import { assertArgument, IDisposable, ArgumentNullError, PromisePal } from '..';

export interface ITraceCategory {
    log(error: Error): void;
    log(text: string): void;
    log(obj: object): void;
}

export type TraceListener = (errorOrText: Error | string | object, category?: string) => void;

export class Trace {
    private static readonly _listeners = new Array<
        (unit: Error | string | object, category?: string) => void
    >();
    public static addListener(
        listener: (errorOrText: Error | string | object, category?: string) => void,
    ): IDisposable {
        assertArgument({ listener }, 'function');

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
            } catch (_) {}
        }
    }

    private static readonly traceCategory = class implements ITraceCategory {
        constructor(private readonly _category: string) {}

        public log(error: Error): void;
        public log(text: string): void;
        public log(obj: object): void;
        public log(arg0: Error | string | object): void {
            for (const listener of Trace._listeners) {
                try {
                    listener(arg0, this._category);
                } catch (_) {}
            }
        }
    };

    public static category(name: string): ITraceCategory {
        assertArgument({ name }, 'string');

        return new Trace.traceCategory(name);
    }

    private static readonly _promiseTrace = Trace.category('promise.traceError');

    private static _cachedEmptyActionOfT<T = unknown>(value: T) {}

    private static traceError<T>(promise: Promise<T>, reason?: any): void {
        Trace._promiseTrace.log(`\r\n\tpromise: ${promise}\r\n\treason: ${reason}`);
    }

    public static async traceErrorRethrow<T = unknown>(promise: Promise<T>): Promise<T> {
        assertArgument({ promise }, Promise);

        return promise.catch(reason => {
            Trace.traceError(promise, reason);
            throw reason;
        });
    }

    public static traceErrorNoThrow<T = unknown>(promise: Promise<T>): Promise<T | void> {
        assertArgument({ promise }, Promise);

        return promise.catch(reason => {
            Trace.traceError(promise, reason);
        });
    }
}
