import { calling, constructing, toJavaScript } from '@test-helpers';
import { NamedPipeClientBuilder } from '@core';
import { ArgumentNullError, ArgumentOutOfRangeError, TimeSpan, Timeout, PublicCtor } from '@foundation';

describe(`surface`, () => {
    describe(`NamedPipeClientBuilder`, () => {
        context(`the constructor`, () => {
            it(`should not throw`, () => {
                class IService { }
                constructing(NamedPipeClientBuilder, 'some pipe', IService)
                    .should.not.throw();
            });

            context(`should throw when called with invalid args`, () => {
                class Case {
                    constructor(
                        public readonly error: new (...args: any[]) => Error,
                        public readonly paramName: string,
                        ...args: any[]) {

                        this.args = args as any;
                    }

                    public readonly args: [string, PublicCtor];

                    private get strArgs(): string { return this.args.map(toJavaScript).join(', '); }
                    public get testTitle(): string { return `new NamedPipeBuilder(${this.strArgs}) should throw ${this.error.name} { paramName: '${this.paramName}' }`; }
                }

                class IService { }

                function* enumerateCases(): Iterable<Case> {
                    yield new Case(ArgumentNullError, 'pipeName');
                    yield new Case(ArgumentNullError, 'pipeName', undefined);
                    yield new Case(ArgumentNullError, 'pipeName', null);
                    yield new Case(ArgumentNullError, 'serviceCtor', 'some pipe');
                    yield new Case(ArgumentNullError, 'serviceCtor', 'some pipe', undefined);
                    yield new Case(ArgumentNullError, 'serviceCtor', 'some pipe', null);

                    yield new Case(ArgumentOutOfRangeError, 'pipeName', 123, IService);
                    yield new Case(ArgumentOutOfRangeError, 'pipeName', true, IService);
                    yield new Case(ArgumentOutOfRangeError, 'pipeName', () => { }, IService);
                    yield new Case(ArgumentOutOfRangeError, 'pipeName', Symbol(), IService);
                    yield new Case(ArgumentOutOfRangeError, 'pipeName', {}, IService);
                    yield new Case(ArgumentOutOfRangeError, 'pipeName', [], IService);

                    yield new Case(ArgumentOutOfRangeError, 'serviceCtor', 'some pipe', 123);
                    yield new Case(ArgumentOutOfRangeError, 'serviceCtor', 'some pipe', true);
                    yield new Case(ArgumentOutOfRangeError, 'serviceCtor', 'some pipe', Symbol());
                    yield new Case(ArgumentOutOfRangeError, 'serviceCtor', 'some pipe', {});
                    yield new Case(ArgumentOutOfRangeError, 'serviceCtor', 'some pipe', []);
                }

                for (const _case of enumerateCases()) {
                    it(_case.testTitle, () => {
                        constructing(NamedPipeClientBuilder, ..._case.args)
                            .should.throw(_case.error)
                            .with.property('paramName', _case.paramName);
                    });
                }
            });
        });

        const withMethodCaseGroups = [
            {
                methodName: 'withRequestTimeout',
                paramName: 'requestTimeout',
                method: NamedPipeClientBuilder.prototype.withRequestTimeout,
                cases: [
                    // should fail
                    { arg: null as any, desc: 'null', error: ArgumentNullError },
                    { arg: undefined as any, desc: 'undefined', error: ArgumentNullError },
                    { arg: TimeSpan.fromDays(-1), desc: 'TimeSpan.fromDays(-1)', error: ArgumentOutOfRangeError },
                    { arg: 'a string', desc: '"a string"', error: ArgumentOutOfRangeError },
                    { arg: false, desc: 'false', error: ArgumentOutOfRangeError },
                    { arg: true, desc: 'true', error: ArgumentOutOfRangeError },

                    // should succeed
                    { arg: TimeSpan.fromMilliseconds(0), desc: 'TimeSpan.fromMilliseconds(0)' },
                    { arg: 0, desc: '0' },
                    { arg: TimeSpan.fromSeconds(2), desc: 'TimeSpan.fromSeconds(2)' },
                    { arg: 2000, desc: '2000' },
                    { arg: TimeSpan.fromHours(5), desc: 'TimeSpan.fromHours(5)' },
                    { arg: 5 * 60 * 60 * 1000, desc: (5 * 60 * 60 * 1000).toString() },
                    { arg: TimeSpan.fromDays(2), desc: 'TimeSpan.fromDays(2)' },
                    { arg: 2 * 24 * 60 * 60 * 1000, desc: (2 * 24 * 60 * 60 * 1000).toString() },
                    { arg: Timeout.infiniteTimeSpan, desc: 'Timeout.infiniteTimeSpan' },
                    { arg: -1, desc: '-1' },
                ],
            },
            {
                methodName: 'withConnectionHook',
                paramName: 'connectionHook',
                method: NamedPipeClientBuilder.prototype.withConnectionHook,
                cases: [
                    // should fail
                    { arg: null as any, desc: 'null', error: ArgumentNullError },
                    { arg: undefined as any, desc: 'undefined', error: ArgumentNullError },
                    { arg: 'a string', desc: '"a string"', error: ArgumentOutOfRangeError },
                    { arg: false, desc: 'false', error: ArgumentOutOfRangeError },
                    { arg: true, desc: 'true', error: ArgumentOutOfRangeError },

                    // should succeed
                    { arg: () => { }, desc: '() => { }' },
                ],
            },
            {
                methodName: 'withMethodCallHook',
                paramName: 'methodCallHook',
                method: NamedPipeClientBuilder.prototype.withMethodCallHook,
                cases: [
                    // should fail
                    { arg: null as any, desc: 'null', error: ArgumentNullError },
                    { arg: undefined as any, desc: 'undefined', error: ArgumentNullError },
                    { arg: 'a string', desc: '"a string"', error: ArgumentOutOfRangeError },
                    { arg: false, desc: 'false', error: ArgumentOutOfRangeError },
                    { arg: true, desc: 'true', error: ArgumentOutOfRangeError },

                    // should succeed
                    { arg: () => { }, desc: '() => { }' },
                ],
            },
            {
                methodName: 'withCallback',
                paramName: 'callback',
                method: NamedPipeClientBuilder.prototype.withCallback,
                cases: [
                    // should fail
                    { arg: null as any, desc: 'null', error: ArgumentNullError },
                    { arg: undefined as any, desc: 'undefined', error: ArgumentNullError },
                    { arg: 'a string', desc: '"a string"', error: ArgumentOutOfRangeError },
                    { arg: false, desc: 'false', error: ArgumentOutOfRangeError },
                    { arg: true, desc: 'true', error: ArgumentOutOfRangeError },

                    // should succeed
                    { arg: {}, desc: '{ }' },
                ],
            },
        ];

        for (const caseGroup of withMethodCaseGroups) {
            context(`the ${caseGroup.methodName} method`, () => {
                for (const _case of caseGroup.cases) {
                    const shouldMessage = _case.error
                        ? `it should throw ${_case.error.name} when called with ${_case.desc}`
                        : `it should succeed when called with ${_case.desc}`;

                    it(shouldMessage, () => {
                        class IService { }
                        const thisArg = new NamedPipeClientBuilder('pipeName', IService);

                        const call = (caseGroup.method as any as () => void).bind(thisArg, _case.arg);
                        if (!_case.error) {
                            call.should.not.throw();
                        } else {
                            call
                                .should.throw(_case.error)
                                .with.property('paramName', caseGroup.paramName);
                        }
                    });
                }
            });
        }

        context(`the build method`, () => {
            it(`shouldn't throw`, () => {
                class IService { }
                const builder = new NamedPipeClientBuilder('pipeName', IService);

                calling(builder.build.bind(builder))
                    .should.not.throw();
            });
        });
    });
});
