import { expect, calling } from '@test-helpers';

import {
    ArgumentNullError,
    ArgumentOutOfRangeError,
    CancellationToken,
    PublicCtor,
} from '@foundation';

import {
    SchemaBuilder,
    Schema,
} from '@core';

import {
    Primitive,
    __returns__,
    __hasName__,
    __hasCancellationToken__,
    __endpoint__,
} from '@core-rtti';

describe(`internals`, () => {
    describe(`SchemaBuilder`, () => {
        context(`the build method`, () => {
            type BuildMethod<T> = (contract: PublicCtor<T>) => Schema;

            it(`should not throw when called with a class ctor`, () => {
                class IContract { }

                calling<BuildMethod<IContract>>(SchemaBuilder.build, IContract)
                    .should.not.throw();
            });

            it(`should throw when called with anything other than a class ctor`, () => {
                calling(SchemaBuilder.build, null as never)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'contract');

                calling(SchemaBuilder.build, undefined as never)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'contract');

                calling(SchemaBuilder.build, {} as never)
                    .should.throw(ArgumentOutOfRangeError, `Specified argument was not of type 'function'.`)
                    .with.property('paramName', 'contract');

                calling(SchemaBuilder.build, true as never)
                    .should.throw(ArgumentOutOfRangeError, `Specified argument was not of type 'function'.`)
                    .with.property('paramName', 'contract');

                calling(SchemaBuilder.build, 123 as never)
                    .should.throw(ArgumentOutOfRangeError, `Specified argument was not of type 'function'.`)
                    .with.property('paramName', 'contract');
            });

            it(`should return an object`, () => {
                class Contract { }

                expect(SchemaBuilder.build(Contract))
                    .to.be.instanceOf(Object);
            });

            it(`should return a schema whose 'endpointName' is the argument of the @__endpointName__ annotation`, () => {
                @__endpoint__('SomeName')
                class Contract { }

                const endpointName = SchemaBuilder.build(Contract).endpointName;
                expect(endpointName).to.be.eq('SomeName');
            });

            it(`should return a schema whose 'endpointName' is the class ctor's name when no @__endpointName__ annotation exists`, () => {
                class SomeContract { }

                const endpointName = SchemaBuilder.build(SomeContract).endpointName;
                expect(endpointName).to.be.eq('SomeContract');
            });

            it('should return a schema whose methods are ', () => {
                const schemaA = SchemaBuilder.build(IContractA);
                expect(schemaA).to.be.deep.eq({
                    endpointName: 'Endpoint',
                    className: 'IContractA',
                    methods: {
                        method1: {
                            hasCancellationToken: true,
                            methodName: 'method1',
                            operationName: 'Method1',
                            returnType: Primitive.void,
                        },
                        Method2: {
                            hasCancellationToken: false,
                            methodName: 'Method2',
                            operationName: 'Method2',
                            returnType: Primitive.string,
                        },
                    },
                });
            });

            @__endpoint__('Endpoint')
            class IContractA {
                @__hasCancellationToken__
                @__hasName__('Method1')
                @__returns__(Primitive.void)
                public method1(ct?: CancellationToken): Promise<void> { throw null; }

                @__returns__(Primitive.string)
                public Method2(): Promise<string> { throw null; }
            }

            class IContractB {
                public method3(): Promise<void> { throw null; }
                public method4(): Promise<void> { throw null; }
            }
        });
    });
});
