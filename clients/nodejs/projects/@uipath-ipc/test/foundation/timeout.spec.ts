import '../jest-extensions';
import { EcmaTimeout } from '../../src/foundation/tasks/ecma-timeout';
import { TimeSpan } from '../../src/foundation/tasks/timespan';
import { ArgumentNullError } from '../../src/foundation/errors/argument-null-error';
import { ArgumentError } from '../../src/foundation/errors/argument-error';
import '../../src/foundation/tasks/promise-pal';

describe('Foundation-Timeout', () => {
    test(`ctor doesn't throw when it shouldn't`, () => {
        let timeout: EcmaTimeout | null = null;
        try {
            expect(() => timeout = new EcmaTimeout(TimeSpan.zero, () => { })).not.toThrow();
        } finally {
            if (timeout) {
                try {
                    timeout.dispose();
                } catch (error) { }
            }
        }
    });
    test(`ctor throws when it should`, () => {
        const timeouts = new Array<EcmaTimeout>();
        try {
            expect(() => timeouts.push(new EcmaTimeout(null as any, () => { }))).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'timespan');
            expect(() => timeouts.push(new EcmaTimeout(null as any, null as any))).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'timespan');
            expect(() => timeouts.push(new EcmaTimeout(TimeSpan.zero, null as any))).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === '_callback');
            expect(() => timeouts.push(new EcmaTimeout(TimeSpan.fromSeconds(-100), () => { }))).toThrowInstanceOf(ArgumentError, error => error.maybeParamName === 'timespan');
        } finally {
            for (const timeout of timeouts) {
                try {
                    timeout.dispose();
                } catch (error) { }
            }
        }
    });
    test(`dispose doesn't throw`, () => {
        const timeout = new EcmaTimeout(TimeSpan.zero, () => { });
        expect(() => timeout.dispose()).not.toThrow();
    });
    test(`Timeout works for 0 milliseconds`, async () => {
        const callback = jest.fn();
        const timeout = new EcmaTimeout(TimeSpan.zero, callback);
        try {
            expect(callback).not.toHaveBeenCalled();
            await Promise.yield();
            expect(callback).toHaveBeenCalledTimes(1);
        } finally {
            timeout.dispose();
        }
    });
    test(`Timeout works for 1 second`, async () => {
        const callback = jest.fn();
        const timeout = new EcmaTimeout(TimeSpan.fromSeconds(1), callback);
        try {
            expect(callback).not.toHaveBeenCalled();
            await Promise.delay(TimeSpan.fromMilliseconds(300));
            expect(callback).not.toHaveBeenCalled();
            await Promise.delay(TimeSpan.fromMilliseconds(700));
            expect(callback).toHaveBeenCalledTimes(1);
        } finally {
            timeout.dispose();
        }
    });
    test(`maybeCreate doesn't throw when it shouldn't`, () => {
        expect(() => EcmaTimeout.maybeCreate(TimeSpan.zero, () => { }).dispose()).not.toThrow();
        expect(() => EcmaTimeout.maybeCreate(null, () => { }).dispose()).not.toThrow();
    });
    test(`maybeCreate throws when it should`, () => {
        expect(() => EcmaTimeout.maybeCreate(TimeSpan.zero, null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'callback');
        expect(() => EcmaTimeout.maybeCreate(null, null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'callback');
    });
});
