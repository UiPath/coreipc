/* istanbul ignore file */
import { TimeSpan } from '../src/foundation/tasks/timespan';

// tslint:disable: no-namespace
// tslint:disable: ban-types

type ErrorCtor<TError extends Error> = new (...args: any[]) => TError;
type Ctor<T> = new (...args: any[]) => T;

export { };

export function _mock_<T>(partial: Partial<T>): T { return partial as any; }

export class MockError extends Error {
    private static computeMessage(id?: string): string { return id ? `MockError { id: '${id}'}` : 'MockError'; }
    constructor(public readonly id?: string) { super(MockError.computeMessage(id)); }
    public toString(): string { return this.message; }
}

declare global {
    namespace jest {
        interface Matchers<R> {
            toThrowSubClassOf<TError extends Error>(errorCtor: ErrorCtor<TError>, maybePredicate?: (error: TError) => boolean): R;
            toThrowInstanceOf<TError extends Error>(errorCtor: ErrorCtor<TError>, maybePredicate?: (error: TError) => boolean): R;
            toThrowInstance<TError extends Error>(errorInstance: TError): R;
            toThrowErrorWhich<TError extends Error>(predicate: (error: TError) => boolean): R;
            toBeMatchedBy<T>(predicate: (value: T) => boolean): R;
            toBeInstanceOf<T>(ctor: Ctor<T>): R;
            toBeInstanceOf<T>(ctor: Ctor<T>, predicate: (value: T) => boolean): R;
            toBeErrorWhich<TError extends Error>(predicate: (value: TError) => boolean): R;
        }
    }
}

export async function measure(func: () => Promise<void>): Promise<TimeSpan> {
    const start = new Date().getTime();
    await func();
    const end = new Date().getTime();
    return TimeSpan.fromMilliseconds(end - start);
}

expect.extend({
    toThrowSubClassOf<TError extends Error>(received: () => any, errorCtor: ErrorCtor<TError>, maybePredicate?: (error: TError) => boolean) {
        try {
            received();
            return {
                message: () => `Should have thrown ${errorCtor.name} but didn't throw anything.`,
                pass: false
            };
        } catch (actual) {
            if (actual instanceof errorCtor) {
                if (!maybePredicate || maybePredicate(actual)) {
                    return {
                        message: () => '',
                        pass: true
                    };
                } else {
                    return {
                        message: () => `Indeed threw ${errorCtor.name} but the error was not matched by predicate ${maybePredicate}.`,
                        pass: false
                    };
                }
            } else {
                return {
                    message: () => `Should have thrown ${errorCtor.name} but threw ${actual.constructor.name}.`,
                    pass: false
                };
            }
        }
    },
    toThrowInstanceOf<TError extends Error>(received: () => any, errorCtor: ErrorCtor<TError>, maybePredicate?: (error: TError) => boolean) {
        try {
            received();
            return {
                message: () => `Should have thrown an instance of ${errorCtor.name} but didn't throw anything.`,
                pass: false
            };
        } catch (actual) {
            if (actual.constructor === errorCtor) {
                if (!maybePredicate || maybePredicate(actual)) {
                    return {
                        message: () => '',
                        pass: true
                    };
                } else {
                    return {
                        message: () => `Indeed threw an instance of ${errorCtor.name} but the error was not matched by predicate ${maybePredicate}.`,
                        pass: false
                    };
                }
            } else {
                return {
                    message: () => `Should have thrown an instance of ${errorCtor.name} but threw ${actual.constructor.name}.`,
                    pass: false
                };
            }
        }
    },
    toThrowInstance<TError extends Error>(received: () => any, errorInstance: TError) {
        try {
            received();
            return {
                message: () => `Should have thrown ${errorInstance} but didn't throw anything.`,
                pass: false
            };
        } catch (actual) {
            if (actual === errorInstance) {
                return {
                    message: () => '',
                    pass: true
                };
            } else {
                return {
                    message: () => `Should have thrown ${errorInstance} but threw an instance of ${actual.constructor.name}.`,
                    pass: false
                };
            }
        }
    },
    toThrowErrorWhich<TError extends Error>(received: () => any, predicate: (error: TError) => boolean) {
        try {
            received();
            return {
                message: () => `Should have thrown an error but didn't throw anything.`,
                pass: false
            };
        } catch (actual) {
            if (predicate(actual)) {
                return {
                    message: () => '',
                    pass: true
                };
            } else {
                return {
                    message: () => `Should have thrown an error which respects a specific predicate but did not.\r\nPredicate: ${predicate}`,
                    pass: false
                };
            }
        }
    },
    toBeMatchedBy<T>(received: T, predicate: (value: T) => boolean) {
        if (predicate(received)) {
            return {
                message: () => `Value ${received} is matched by predicate.`,
                pass: true
            };
        } else {
            return {
                message: () => `Value ${received} is not matched by predicate.`,
                pass: false
            };
        }
    },
    toBeInstanceOf<T>(received: any, ctor: Ctor<T>, maybePredicate?: (value: T) => boolean) {
        if (received instanceof ctor) {
            if (!maybePredicate || maybePredicate(received)) {
                return {
                    message: () => '',
                    pass: true
                };
            } else {
                return {
                    message: () => `Received ${received} which is indeed of type ${ctor.name} but doesn't match the specified predicate ${maybePredicate}`,
                    pass: false
                };
            }
        } else {
            return {
                message: () => `Received ${received} which should be of type ${ctor.name} but is of type ${(received as any).constructor.name}`,
                pass: false
            };
        }
    },
    toBeErrorWhich<TError extends Error>(received: TError, predicate: (value: TError) => boolean) {
        if (predicate(received)) {
            return {
                message: () => `Error ${received} is matched by predicate.`,
                pass: true
            };
        } else {
            return {
                message: () => `Error ${received} is not matched by predicate.`,
                pass: false
            };
        }
    }
});
