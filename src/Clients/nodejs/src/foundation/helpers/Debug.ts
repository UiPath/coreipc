import { InvalidOperationError } from '../errors/InvalidOperationError';
import { Observable, Subject } from 'rxjs';

export class Debug implements ITrace {
    public static enabled = false;
    public static trace: ITrace = new Debug();

    public static category(key: string | symbol | number | undefined): IDebugCategory {
        if (key == null) { return Debug; }

        let result = Debug._categories.get(key);
        if (!result) {
            result = new Debug.debugCategory(key);
            Debug._categories.set(key, result);
        }
        return result;
    }

    public static readonly log = new Array<any>();
    private static readonly _$log = new Subject<any>();
    public static get $log(): Observable<any> { return Debug._$log; }

    private static readonly _categories = new Map<string | symbol | number, IDebugCategory>();

    public call(method: string, args: any[], object?: any): ICallTrace {
        const callTrace = new Debug.callTrace(object, method, args);
        Debug.log.push(callTrace);
        Debug._$log.next(callTrace);
        return callTrace;
    }

    private static readonly callTrace = class implements ICallTrace {
        constructor(
            public readonly object: any | undefined,
            public readonly method: string,
            public readonly args: readonly any[],
        ) { }

        public syncResult: any | undefined;
        public syncError: any | undefined;

        public asyncResult: any | undefined;
        public asyncError: any | undefined;

        public finishedSync: Date | undefined;
        public finishedAsync: Date | undefined;
        public isAsync: boolean | undefined;
        public status: CallStatus = CallStatus.Running;
        public startedAt: Date = new Date();

        public succeededSync(result?: unknown): void {
            this.finishedSync = new Date();
            this.syncResult = result;
            this.isAsync = result instanceof Promise;
            this.status = this.isAsync ? CallStatus.RunningAsync : CallStatus.SucceededSync;
        }
        public failedSync(error?: unknown): void {
            this.finishedSync = new Date();
            this.syncError = error;
            this.status = CallStatus.FailedSync;
        }
        public succeededAsync(result?: unknown): void {
            this.finishedAsync = new Date();
            this.asyncResult = result;
            this.status = CallStatus.SucceededAsync;
        }
        public failedAsync(error?: unknown): void {
            this.finishedAsync = new Date();
            this.asyncError = error;
            this.status = CallStatus.FailedAsync;
        }
    };

    private static readonly debugCategory = class implements IDebugCategory, ITrace {
        constructor(
            public readonly categoryName?: string | number | symbol,
        ) { }

        public enabled: boolean = true;
        public readonly trace: ITrace = this;

        public call(method: string, args: any[], object?: any): ICallTrace {
            const callTrace = new Debug.callTrace(object, method, args);
            this.log.push(callTrace);
            this.$log.next(callTrace);
            return callTrace;
        }

        public readonly log = new Array<any>();
        public readonly $log = new Subject<any>();
    };
}

export interface ITrace {
    call(method: string, args: any[], object?: any): ICallTrace;
}

export interface ICallTrace {
    readonly object: any | undefined;
    readonly method: string;
    readonly args: readonly any[];
    readonly status: CallStatus;

    readonly startedAt: Date;
    readonly finishedSync: Date | undefined;
    readonly finishedAsync: Date | undefined;
    readonly isAsync: boolean | undefined;

    readonly syncResult: any | undefined;
    readonly syncError: any | undefined;

    readonly asyncResult: any | undefined;
    readonly asyncError: any | undefined;

    succeededSync(result?: unknown): void;
    failedSync(error?: unknown): void;
    succeededAsync(result?: unknown): void;
    failedAsync(error?: unknown): void;
}

export enum CallStatus {
    Running,
    RunningAsync,
    SucceededSync,
    SucceededAsync,
    FailedSync,
    FailedAsync,
}

export interface IDebugCategory {
    enabled: boolean;
    readonly categoryName?: string | symbol | number;
    readonly trace: ITrace;
    readonly log: readonly any[];
    readonly $log: Observable<any>;
}

declare global {
    interface Object {
        debug<T, Key extends keyof T & string>(this: T, method: Key, category?: IDebugCategory): DecoratedDelegate<T, Key>;
    }

    interface Array<T> {
        debug<Key extends keyof T[] & string>(this: T[], method: Key, category?: IDebugCategory): DecoratedDelegate<T[], Key>;
    }

    interface Function {
        debug<T, Key extends keyof T & string>(this: T, method: Key, category?: IDebugCategory): DecoratedDelegate<T, Key>;
    }
}

export type DecoratedDelegate<T, Key extends keyof T & string>
    = (...args: T[Key] extends (...args: infer P) => any ? P : never)
        => T[Key] extends (...args: any[]) => infer R ? R : never;

Object.prototype.debug = function <T, Key extends keyof T & string>(this: T, method: Key, category?: IDebugCategory): DecoratedDelegate<T, Key> {
    const target = this[method] as any as DecoratedDelegate<T, Key>;
    if (typeof target !== 'function') { throw new InvalidOperationError(); }
    const boundTarget = target.bind(this as any);
    category = category ?? Debug;
    if (!category.enabled) { return boundTarget; }

    return (...args: T[Key] extends (...args: infer P) => any ? P : never): T[Key] extends (...args: any[]) => infer R ? R : never => {
        category = category ?? Debug;
        const callTrace = category.trace.call(method, args, this);

        try {
            const syncResult = target(...args);
            callTrace.succeededSync(syncResult);

            if (syncResult instanceof Promise) {
                const _ = (async () => {
                    try {
                        callTrace.succeededAsync(await syncResult);
                    } catch (asyncError) {
                        callTrace.failedAsync(asyncError);
                    }
                })();
            }

            return syncResult;
        } catch (syncError) {
            callTrace.failedSync(syncError);
            throw syncError;
        }
    };
};
