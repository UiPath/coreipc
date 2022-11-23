import {
    AggregateDisposable,
    IDisposable,
    Trace,
    TraceListener,
    ITraceCategory,
    PromisePal,
} from '../../../src/std';

import { expect } from 'chai';
import { __mentionsParameter } from '../../infrastructure/__parameters';

import pluralize from 'pluralize';

describe(`${Trace.name}'s`, () => {
    describe(`📞 "addListener" static method`, () => {
        describe(`should throw when called with invalid args`, () => {
            const argsList = [[], [123]] as any as Parameters<typeof Trace.addListener>[];

            for (const args of argsList) {
                it(`when called with [${JSON.stringify(args)}]`, () => {
                    const act = () => Trace.addListener(...args);

                    expect(act).to.throw().that.satisfies(__mentionsParameter('listener'));
                });
            }
        });

        it(`should not throw when called with valid args`, () => {
            const act = () => {
                const disposable = Trace.addListener(() => {});
                try {
                    disposable.dispose();
                } catch {}
            };

            expect(act).not.to.throw();
        });

        it(`should return a disposable`, () => {
            let disposable: any = undefined;
            try {
                disposable = Trace.addListener(() => {});
                expect(disposable).to.haveOwnProperty('dispose').that.is.instanceOf(Function);
            } finally {
                disposable?.dispose();
            }
        });
    });

    describe(`📞 "addListener" static method's returned value's`, () => {
        it(`should be a disposable`, () => {
            let disposable: any = undefined;
            try {
                disposable = Trace.addListener(() => {});
                expect(disposable).to.haveOwnProperty('dispose').that.is.instanceOf(Function);
            } finally {
                disposable?.dispose();
            }
        });

        describe(`📞 "dispose" instance method`, () => {
            it(`should not throw`, () => {
                const listener = () => {};
                const disposable = Trace.addListener(listener);

                const act = () => disposable.dispose();
                expect(act).not.to.throw();
            });

            it(`should unregister the listener`, () => {
                const mockMessage = 'foo';
                let callCount = 0;
                const listener = () => callCount++;

                Trace.log(mockMessage);
                expect(callCount).to.equal(0);

                const disposable = Trace.addListener(listener);

                Trace.log(mockMessage);
                expect(callCount).to.equal(1);

                disposable.dispose();

                Trace.log(mockMessage);
                expect(callCount).to.equal(1);
            });

            it(`should not throw when called a 2nd time`, () => {
                const listener = () => {};
                const disposable = Trace.addListener(listener);

                disposable.dispose();
                const act = () => disposable.dispose();
                expect(act).not.to.throw();
            });
        });
    });

    describe(`📞 "log" static method`, () => {
        const properArgsList = [
            [new Error()],
            ['message'],
            [{ x: 123, y: true, z: 'test' }],
        ] as Parameters<typeof Trace.log>[];

        const invalidArgsList = [[], [true], [true, true], [123], [{}, {}]] as any as Parameters<
            typeof Trace.log
        >[];

        const argsList = [...properArgsList, ...invalidArgsList];

        const listenerCounts = [0, 1, 2];

        describe(`should not throw for any args`, () => {
            for (const length of listenerCounts) {
                describe(`when there are ${length} ${pluralize(
                    'listener',
                    length,
                )} registered`, () => {
                    for (const args of argsList) {
                        it(`when called with args ${JSON.stringify(args)}`, () => {
                            const disposables = AggregateDisposable.maybeCreate(
                                ...Array.from({ length }, () => Trace.addListener(() => {})),
                            );

                            try {
                                const act = () => Trace.log(...args);
                                expect(act).not.to.throw();
                            } finally {
                                disposables?.dispose();
                            }
                        });
                    }
                });
            }
        });

        describe(`should pass the 1st argument to all registered listeners`, () => {
            class Mock {
                public static register(mock: Mock): IDisposable {
                    return Trace.addListener(mock.listen.bind(mock));
                }

                public listen(errorOrText: Error | string | object, category?: string): void {
                    this._calls.push([errorOrText, category]);
                }

                public get calls(): Parameters<TraceListener>[] {
                    return [...this._calls];
                }

                private readonly _calls = new Array<Parameters<TraceListener>>();
            }

            for (const length of listenerCounts) {
                if (length === 0) {
                    continue;
                }
                describe(`when there are ${length} ${pluralize(
                    'listener',
                    length,
                )} registered`, () => {
                    for (const args of argsList) {
                        it(`when called with args ${JSON.stringify(args)}`, () => {
                            const mocks = Array.from({ length }, () => new Mock());
                            const disposables = new AggregateDisposable(
                                ...mocks.map(Mock.register),
                            );

                            try {
                                Trace.log(...args);

                                mocks.forEach(mock => {
                                    const calls = mock.calls;
                                    expect(calls).to.have.lengthOf(1);
                                    expect(calls[0][0]).to.deep.equal(args[0]);
                                    expect(calls[0][1]).to.be.undefined;
                                });
                            } finally {
                                disposables?.dispose();
                            }
                        });
                    }
                });
            }
        });

        it(`should hide errors thrown by registered listeners`, () => {
            const error = new Error();
            const message = 'foo';

            const disposable = Trace.addListener(() => {
                throw error;
            });

            try {
                const act = () => Trace.log(message);
                expect(act).not.to.throw();
            } finally {
                disposable.dispose();
            }
        });
    });

    describe(`📞 "category" static method`, () => {
        describe(`should throw for invalid args`, () => {
            const argsList = [[], [123], [true], [{}]] as Parameters<typeof Trace.category>[];

            for (const args of argsList) {
                it(`when called with ${JSON.stringify(args)}`, () => {
                    const act = () => Trace.category(...args);
                    expect(act).to.throw();
                });
            }
        });

        it(`should not throw when called with valid args`, () => {
            const act = () => Trace.category('foo');
            expect(act).not.to.throw();
        });
    });

    describe(`📞 "category" static method's returned value`, () => {
        it(`should be an ITraceCategory`, () => {
            expect(Trace.category('foo'))
                .to.be.instanceOf(Object)
                .which.satisfies((x: any) => {
                    expect(x.log).to.be.instanceOf(Function);
                    return true;
                });
        });

        describe(`📞 "log" instance method`, () => {
            describe(`it should not throw for any args`, () => {
                const argsList = [[], [123], [true], [{}]] as Parameters<typeof Trace.category>[];

                for (const args of argsList) {
                    it(`when called with ${JSON.stringify(args)}`, () => {
                        const act = () => Trace.category(...args);
                        expect(act).to.throw();
                    });
                }
            });

            describe(`should pass the 1st argument to all registered listeners`, () => {
                const properArgsList = [
                    [new Error()],
                    ['message'],
                    [{ x: 123, y: true, z: 'test' }],
                ] as Parameters<ITraceCategory['log']>[];

                const invalidArgsList = [
                    [],
                    [true],
                    [true, true],
                    [123],
                    [{}, {}],
                ] as any as Parameters<ITraceCategory['log']>[];

                const argsList = [...properArgsList, ...invalidArgsList];

                const listenerCounts = [1, 2];

                class Mock {
                    public static register(mock: Mock): IDisposable {
                        return Trace.addListener(mock.listen.bind(mock));
                    }

                    public listen(errorOrText: Error | string | object, category?: string): void {
                        this._calls.push([errorOrText, category]);
                    }

                    public get calls(): Parameters<TraceListener>[] {
                        return [...this._calls];
                    }

                    private readonly _calls = new Array<Parameters<TraceListener>>();
                }

                const mockCategory = 'some-category';

                for (const length of listenerCounts) {
                    describe(`when there are ${length} ${pluralize(
                        'listener',
                        length,
                    )} registered`, () => {
                        for (const args of argsList) {
                            it(`when called with args ${JSON.stringify(args)}`, () => {
                                const mocks = Array.from({ length }, () => new Mock());
                                const disposables = new AggregateDisposable(
                                    ...mocks.map(Mock.register),
                                );

                                try {
                                    Trace.category(mockCategory).log(...args);

                                    mocks.forEach(mock => {
                                        const calls = mock.calls;
                                        expect(calls).to.have.lengthOf(1);
                                        expect(calls[0][0]).to.deep.equal(args[0]);
                                        expect(calls[0][1]).to.equal(mockCategory);
                                    });
                                } finally {
                                    disposables?.dispose();
                                }
                            });
                        }
                    });
                }
            });

            it(`should hide errors thrown by registered listeners`, () => {
                const error = new Error();
                const message = 'foo';
                const category = 'bar';

                const disposable = Trace.addListener(() => {
                    throw error;
                });

                try {
                    const act = () => Trace.category(category).log(message);
                    expect(act).not.to.throw();
                } finally {
                    disposable.dispose();
                }
            });
        });
    });

    describe(`📞 "traceError" static method`, () => {
        describe(`should throw for invalid args`, () => {
            const argsList = [[], [123], [true], [{}]] as Parameters<typeof Trace.traceError>[];
            for (const args of argsList) {
                it(`when called with ${JSON.stringify(args)}`, () => {
                    const act = () => Trace.traceError(...args);
                    expect(act).to.throw();
                });
            }
        });

        it(`should not throw when called with valid args`, () => {
            const act = () =>
                Trace.traceError(
                    new Promise<boolean>((resolve, reject) => {
                        resolve(true);
                    }),
                );
            expect(act).not.to.throw();
        });

        const promiseTraceErrorCategory = 'promise.traceError';

        it(`should trace Promise rejections on the "${promiseTraceErrorCategory}" category`, async () => {
            const calls = new Array<Parameters<TraceListener>>();

            Trace.addListener((error, category) => {
                calls.push([error, category]);
            });

            let reject: ((reason: any) => void) | undefined = undefined;

            Trace.traceError(
                new Promise<number>((_, x) => {
                    reject = x;
                }),
            );

            await PromisePal.delay(1);

            expect(reject).to.not.be.null.and.not.be.undefined;
            expect(calls).to.have.lengthOf(0);

            const error = new Error();

            reject!(error);

            await PromisePal.delay(1);

            expect(calls).to.have.lengthOf(1);
            expect(calls[0][1]).to.equal(promiseTraceErrorCategory);
        });
    });
});
