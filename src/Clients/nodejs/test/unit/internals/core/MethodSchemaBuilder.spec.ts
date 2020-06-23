import { expect, calling } from '@test-helpers';
import {
    ArgumentNullError,
    ArgumentOutOfRangeError,
    CancellationToken,
    PublicCtor,
} from '@foundation';
import {
    MethodSchemaBuilder,
    MethodSchema,
    MemberName,
} from '@core';
import { __returns__, Primitive, __hasName__, __hasCancellationToken__ } from '@core-rtti';

describe(`internals`, () => {
    describe(`MethodSchemaBuilder`, () => {
        context(`the build method`, () => {
            type BuildMethod<TContract> = (contract: PublicCtor<TContract>, methodName: MemberName<TContract>) => MethodSchema;

            it(`should not throw`, () => {
                class IContract {
                    public sum(x: number, y: number): Promise<number> { throw null; }
                }

                calling<BuildMethod<IContract>>(MethodSchemaBuilder.build, IContract, 'sum')
                    .should.not.throw();
            });

            it(`should not throw even if no such method exists`, () => {
                class IContract { }

                calling(MethodSchemaBuilder.build, IContract, 'inexistentMethod' as never)
                    .should.not.throw();
            });

            it(`should return an object`, () => {
                class IContract {
                    public method(): Promise<void> { throw null; }
                }

                expect(MethodSchemaBuilder.build(IContract, 'method'))
                    .to.be.instanceOf(Object);
            });

            it(`should return an object even if no such method exists`, () => {
                class IContract {
                }

                expect(MethodSchemaBuilder.build(IContract, 'inexistentMethod' as never))
                    .to.be.instanceOf(Object);
            });

            it(`should consider the @__hasCancellationToken__ annotation`, () => {
                class IContract {
                    public method1(): void { throw null; }

                    @__hasCancellationToken__
                    public method2(ct?: CancellationToken): void { throw null; }
                }

                expect(MethodSchemaBuilder.build(IContract, 'method1').hasCancellationToken).to.be.eq(false);
                expect(MethodSchemaBuilder.build(IContract, 'method2').hasCancellationToken).to.be.eq(true);
            });

            it(`should consider the @__hasName__ annotation when deciding the operationName in the MethodSchema`, () => {
                class IContract {
                    public method1(): void { throw null; }

                    @__hasName__('method3')
                    public method2(ct?: CancellationToken): void { throw null; }
                }

                MethodSchemaBuilder.build(IContract, 'method1').should.satisfy((methodSchema: MethodSchema) => {
                    expect(methodSchema.methodName).to.be.eq('method1');
                    expect(methodSchema.operationName).to.be.eq('method1');
                    return true;
                });

                MethodSchemaBuilder.build(IContract, 'method2').should.satisfy((methodSchema: MethodSchema) => {
                    expect(methodSchema.methodName).to.be.eq('method2');
                    expect(methodSchema.operationName).to.be.eq('method3');
                    return true;
                });
            });

            it(`should consider the @__returnType__ annotation`, () => {
                class Complex {
                    constructor(
                        public readonly x: number,
                        public readonly y: number,
                    ) { }
                }

                class IContract {
                    public print(line: string): Promise<boolean> { throw null; }

                    @__returns__(Primitive.number)
                    public sum(x: number, y: number): Promise<number> { throw null; }

                    @__returns__(Complex)
                    public sumComplex(x: Complex, y: Complex): Promise<Complex> { throw null; }
                }

                MethodSchemaBuilder.build(IContract, 'print').should.have.property('returnType', null);
            });

            it(`should throw when called with a falsy contract`, () => {
                for (const contract of [null, undefined]) {
                    calling(MethodSchemaBuilder.build, contract as any, 'name' as never)
                        .should.throw(ArgumentNullError, undefined, `contract === ${contract}`)
                        .with.property(
                            'paramName',
                            'contract');
                }
            });

            it(`should throw when called with a contract arg which isn't a function`, () => {
                for (const contract of [123, 'a string', true, {}]) {
                    calling(MethodSchemaBuilder.build, contract as any, 'name' as never)
                        .should.throw(
                            ArgumentOutOfRangeError,
                            `Specified argument was not of type 'function'.`,
                            `contract === ${contract}`)
                        .with.property(
                            'paramName',
                            'contract');
                }
            });

            it(`should throw when called with a null methodName`, () => {
                class IContract { }

                calling<BuildMethod<IContract>>(MethodSchemaBuilder.build, IContract, null as never)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'methodName');
            });

            it(`should throw when called with an empty string methodName`, () => {
                class IContract { }

                calling<BuildMethod<IContract>>(MethodSchemaBuilder.build, IContract, '' as never)
                    .should.throw(ArgumentOutOfRangeError, 'Specified argument was an empty string.')
                    .with.property('paramName', 'methodName');
            });

            it(`should not throw when called with an undefined methodName and return a function instead of an object`, () => {
                class IContract { }

                calling<BuildMethod<IContract>>(MethodSchemaBuilder.build, IContract, undefined as never)
                    .should.not.throw();
            });

            it(`should return a function when called with an undefined methodName`, () => {
                class IContract { }

                expect(MethodSchemaBuilder.build(IContract)).to.be.a('function');
            });

            it(`should throw when called with a methodName which isn't a string`, () => {
                class IContract { }

                for (const methodName of [true, 123, {}, () => { }]) {
                    calling<BuildMethod<IContract>>(MethodSchemaBuilder.build, IContract, methodName as never)
                        .should.throw(
                            ArgumentOutOfRangeError,
                            `Specified argument was not of type 'string'.`,
                            `methodName === ${methodName}`)
                        .with.property(
                            'paramName',
                            'methodName');
                }
            });
        });

        context(`the function returned by the build method when called without a methodName`, () => {
            it(`should return an object`, () => {
                class IContract {
                    public sum(x: number, y: number): Promise<number> { throw null; }
                }

                expect(MethodSchemaBuilder.build(IContract)('sum'))
                    .to.be.instanceOf(Object);
            });

            it(`should not throw`, () => {
                class IContract {
                    public method(): Promise<void> { throw null; }
                }

                calling(MethodSchemaBuilder.build(IContract), 'method')
                    .should.not.throw();
            });

            it(`should not throw even if no such method exists`, () => {
                class IContract { }

                calling(MethodSchemaBuilder.build(IContract), 'inexistentMethod' as never)
                    .should.not.throw();
            });

            it(`should return an object even if no such method exists`, () => {
                class IContract {
                }

                expect(MethodSchemaBuilder.build(IContract)('inexistentMethod' as never))
                    .to.be.instanceOf(Object);
            });

            // ---

            it(`should consider the @__hasCancellationToken__ annotation`, () => {
                class IContract {
                    public method1(): void { throw null; }

                    @__hasCancellationToken__
                    public method2(_ct?: CancellationToken): void { throw null; }
                }

                const obtainedMethod = MethodSchemaBuilder.build(IContract);

                expect(obtainedMethod('method1').hasCancellationToken).to.be.eq(false);
                expect(obtainedMethod('method2').hasCancellationToken).to.be.eq(true);
            });

            it(`should consider the @__hasName__ annotation when deciding the operationName in the MethodSchema`, () => {
                class IContract {
                    public method1(): void { throw null; }

                    @__hasName__('someOtherName')
                    public method2(_ct?: CancellationToken): void { throw null; }
                }

                const obtainedMethod = MethodSchemaBuilder.build(IContract);

                obtainedMethod('method1').operationName.should.be.eq('method1');
                obtainedMethod('method2').operationName.should.be.eq('someOtherName');
            });

            it(`should consider the @__returnType__ annotation`, () => {
                class Complex {
                    constructor(
                        public readonly x: number,
                        public readonly y: number,
                    ) { }
                }

                class IContract {
                    public print(_line: string): Promise<boolean> { throw null; }

                    @__returns__(Primitive.number)
                    public sum(_x: number, _y: number): Promise<number> { throw null; }

                    @__returns__(Complex)
                    public sumComplex(_x: Complex, _y: Complex): Promise<Complex> { throw null; }
                }

                MethodSchemaBuilder.build(IContract)('print').should.have.property('returnType', null);
                MethodSchemaBuilder.build(IContract)('sum').should.have.property('returnType', Primitive.number);
                MethodSchemaBuilder.build(IContract)('sumComplex').should.have.property('returnType', Complex);
            });

            it(`should throw when called with a null or undefined methodName`, () => {
                class IContract { }

                for (const methodName of [null, undefined]) {
                    calling(MethodSchemaBuilder.build(IContract), methodName as never)
                        .should.throw(ArgumentNullError)
                        .with.property('paramName', 'methodName');
                }
            });

            it(`should throw when called with an empty string methodName`, () => {
                class IContract { }

                calling(MethodSchemaBuilder.build(IContract), '' as never)
                    .should.throw(ArgumentOutOfRangeError, 'Specified argument was an empty string.')
                    .with.property('paramName', 'methodName');
            });

            it(`should throw when called with a methodName which isn't a string`, () => {
                class IContract { }

                for (const methodName of [true, 123, {}, () => { }]) {
                    calling(MethodSchemaBuilder.build(IContract), methodName as never)
                        .should.throw(
                            ArgumentOutOfRangeError,
                            `Specified argument was not of type 'string'.`,
                            `methodName === ${methodName}`)
                        .with.property(
                            'paramName',
                            'methodName');
                }
            });
        });
    });
});
