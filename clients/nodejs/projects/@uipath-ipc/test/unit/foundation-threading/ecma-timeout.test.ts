// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, should, spy, use } from 'chai';
import spies from 'chai-spies';

import { EcmaTimeout, TimeSpan } from '@foundation/threading';
import { ArgumentError, ArgumentNullError } from '@foundation/errors';

use(spies);

describe(`foundation:threading -> class:EcmaTimeout`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw when provided valid args`, () => {
            expect(() => new EcmaTimeout(TimeSpan.fromMilliseconds(1), () => { })).not.to.throw();
        });
        it(`should throw when provided a negative timespan`, () => {
            expect(() => new EcmaTimeout(TimeSpan.fromMilliseconds(-1), () => { })).to.throw(ArgumentError).property('maybeParamName', 'timespan');
        });
        it(`should throw when provided a falsy timespan`, () => {
            expect(() => new EcmaTimeout(null as any, () => { })).to.throw(ArgumentNullError).property('maybeParamName', 'timespan');
            expect(() => new EcmaTimeout(undefined as any, () => { })).to.throw(ArgumentNullError).property('maybeParamName', 'timespan');
        });
        it(`should throw when provided a falsy callback`, () => {
            expect(() => new EcmaTimeout(TimeSpan.fromMilliseconds(1), null as any)).to.throw(ArgumentNullError).property('maybeParamName', '_callback');
            expect(() => new EcmaTimeout(TimeSpan.fromMilliseconds(1), undefined as any)).to.throw(ArgumentNullError).property('maybeParamName', '_callback');
        });
        it(`should throw when provided both a falsy timespan and a falsy callback`, () => {
            expect(() => new EcmaTimeout(null as any, null as any)).to.throw(ArgumentNullError).property('maybeParamName', 'timespan');
            expect(() => new EcmaTimeout(null as any, undefined as any)).to.throw(ArgumentNullError).property('maybeParamName', 'timespan');
            expect(() => new EcmaTimeout(undefined as any, null as any)).to.throw(ArgumentNullError).property('maybeParamName', 'timespan');
            expect(() => new EcmaTimeout(undefined as any, undefined as any)).to.throw(ArgumentNullError).property('maybeParamName', 'timespan');
        });
    });

    const _timeSpans = [
        TimeSpan.fromMilliseconds(0),
        TimeSpan.fromMilliseconds(100),
        TimeSpan.fromMilliseconds(200),
        TimeSpan.fromMilliseconds(300)
    ];

    it(`should cause the callback's execution`, async () => {
        for (const timeSpan of _timeSpans) {
            const spyHandler = spy(() => { });
            const ecmaTimeout = new EcmaTimeout(timeSpan, spyHandler);

            expect(spyHandler).not.to.have.been.called();

            if (timeSpan.totalMilliseconds >= 10) {
                await Promise.yield();
                expect(spyHandler).not.to.have.been.called();

                await Promise.delay(timeSpan.totalMilliseconds / 20);
                expect(spyHandler).not.to.have.been.called();
            }

            await Promise.delay(timeSpan.totalMilliseconds * 2);
            expect(spyHandler).to.have.been.called();
        }
    });

    context(`method:dispose`, () => {
        it(`shouldn't throw (even if called multiple times)`, async () => {
            const ecmaTimeout = new EcmaTimeout(TimeSpan.fromMilliseconds(0), () => { });
            expect(() => ecmaTimeout.dispose()).not.to.throw();
            expect(() => ecmaTimeout.dispose()).not.to.throw();
            await Promise.yield();
            expect(() => ecmaTimeout.dispose()).not.to.throw();
            expect(() => ecmaTimeout.dispose()).not.to.throw();
        });
        it(`successfully cancels the timeout`, async () => {
            const spyHandler = spy(() => { });
            const ecmaTimeout = new EcmaTimeout(TimeSpan.fromMilliseconds(0), spyHandler);
            expect(spyHandler).not.to.have.been.called;
            ecmaTimeout.dispose();
            await Promise.yield();
            expect(spyHandler).not.to.have.been.called;
        });
    });

    context(`method:maybeCreate`, () => {
        it(`should throw when provided a falsy callback`, () => {
            expect(() => EcmaTimeout.maybeCreate(null, null as any)).to.throw(ArgumentNullError).property('maybeParamName', 'callback');
            expect(() => EcmaTimeout.maybeCreate(null, undefined as any)).to.throw(ArgumentNullError).property('maybeParamName', 'callback');
        });

        const _cases: Array<{
            timeSpan: TimeSpan | null | undefined,
            isEffective?: boolean
        }> = [
                {
                    timeSpan: TimeSpan.fromMilliseconds(20),
                    isEffective: true
                },
                {
                    timeSpan: TimeSpan.fromMilliseconds(0),
                    isEffective: true
                },
                {
                    timeSpan: TimeSpan.fromMilliseconds(-1)
                },
                {
                    timeSpan: null
                },
                {
                    timeSpan: undefined
                }
            ];

        it(`shouldn't throw when provided valid args (even a negative timespan)`, () => {
            for (const _case of _cases) {
                expect(() => EcmaTimeout.maybeCreate(_case.timeSpan as any, () => { })).not.to.throw();
            }
        });
        it(`should return a truthy reference`, () => {
            for (const _case of _cases) {
                const result = EcmaTimeout.maybeCreate(_case.timeSpan as any, () => { });

                expect(result).not.to.be.null;
                expect(result).not.to.be.undefined;
            }
        });
        it(`should return an IDisposable`, () => {
            for (const _case of _cases) {
                const result = EcmaTimeout.maybeCreate(_case.timeSpan as any, () => { });

                expect(result).property('dispose').instanceOf(Function);
            }
        });
        it(`should cause the callback's execution when the provided timespan is defined, non-null and non-negative`, async () => {
            for (const _case of _cases.filter(x => !!x.isEffective)) {
                const spyHandler = spy(() => { });
                const result = EcmaTimeout.maybeCreate(_case.timeSpan as any, spyHandler);

                expect(spyHandler).not.to.have.been.called;
                const timeSpan = _case.timeSpan as any as TimeSpan;
                await Promise.delay(timeSpan.add(timeSpan));
                expect(spyHandler).to.have.been.called;
            }
        });
        it(`should return an IDisposable which when used will cancel the callback's execution (when the provided timespan is defined, non-null and non-negative)`, async () => {
            for (const _case of _cases.filter(x => !!x.isEffective)) {
                const spyHandler = spy(() => { });
                const disposable = EcmaTimeout.maybeCreate(_case.timeSpan as any, spyHandler);

                expect(spyHandler).not.to.have.been.called;
                disposable.dispose();
                const timeSpan = _case.timeSpan as any as TimeSpan;
                await Promise.delay(timeSpan.add(timeSpan));
                expect(spyHandler).not.to.have.been.called;
            }
        });
        it(`shouldn't cause the callback's execution when the provided timespan is undefined, null or negative`, async () => {
            for (const _case of _cases.filter(x => !x.isEffective)) {
                const spyHandler = spy(() => { });
                const result = EcmaTimeout.maybeCreate(_case.timeSpan as any, spyHandler);

                expect(spyHandler).not.to.have.been.called;
                await Promise.yield();
                expect(spyHandler).not.to.have.been.called;
            }
        });
    });
});
