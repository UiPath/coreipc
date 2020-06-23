import { expect, calling } from '@test-helpers';
import { MethodNameEnumerator, ArgumentNullError, ArgumentOutOfRangeError } from '@foundation';
import { __returns__, __hasName__, __hasCancellationToken__ } from '@core-rtti';

describe(`internals`, () => {
    describe(`MethodNameEnumerator`, () => {
        context(`the enumerate method`, () => {
            it(`should not throw when called with a class ctor`, () => {
                class Class { }

                calling(MethodNameEnumerator.enumerate, Class)
                    .should.not.throw();
            });

            it(`should throw when called with anything other than a class ctor`, () => {
                calling(MethodNameEnumerator.enumerate, null as any)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'ctor');

                calling(MethodNameEnumerator.enumerate, undefined as any)
                    .should.throw(ArgumentNullError)
                    .with.property('paramName', 'ctor');

                calling(MethodNameEnumerator.enumerate, {} as any)
                    .should.throw(ArgumentOutOfRangeError, `Specified argument was not of type 'function'.`)
                    .with.property('paramName', 'ctor');

                calling(MethodNameEnumerator.enumerate, true as any)
                    .should.throw(ArgumentOutOfRangeError, `Specified argument was not of type 'function'.`)
                    .with.property('paramName', 'ctor');

                calling(MethodNameEnumerator.enumerate, 123 as any)
                    .should.throw(ArgumentOutOfRangeError, `Specified argument was not of type 'function'.`)
                    .with.property('paramName', 'ctor');
            });

            it(`should return an array`, () => {
                class Class1 { }
                expect(MethodNameEnumerator.enumerate(Class1)).to.be.an('array');

                class Class2 {
                    public A(): void { }
                    public B(): void { }
                    public C(): void { }
                }
                expect(MethodNameEnumerator.enumerate(Class2)).to.be.an('array');
            });

            it(`should return all method names`, () => {
                class Class1 { }
                MethodNameEnumerator.enumerate(Class1).should.be.deep.eq([]);

                class Class2 {
                    public A(): void { }
                    public B(): void { }
                    public C(): void { }
                }
                MethodNameEnumerator.enumerate(Class2).should.be.deep.eq(['A', 'B', 'C']);
            });

            it(`shouldn't return names of other member types`, () => {
                class Class {
                    public A(): void { }
                    public B = new Function();
                    public C = () => { };

                    public D = 123;
                    public E = true;
                    public F = Symbol();
                    public G = 'foo';
                    public H = {};
                    public I = undefined;

                    public static J = 123;
                    public static K = true;
                    public static L = Symbol();
                    public static M = 'foo';
                    public static N = {};
                    public static O = undefined;
                }

                MethodNameEnumerator.enumerate(Class)
                    .should.not.include.members(
                        ['D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O'],
                    ).but.include.members(
                        ['A', 'B', 'C'],
                    );
            });

            it(`shouldn't return keys that aren't strings`, () => {
                const $a = Symbol();

                class Class {
                    public [$a]() { }
                    public [123]() { }
                    public Method() { }
                }

                Reflect.ownKeys(Class.prototype).should.include.members(
                    ['123', 'Method', $a],
                );

                MethodNameEnumerator.enumerate(Class)
                    .should.include('Method')
                    .but.not.include.members(['123', $a])
                    ;
            });
        });
    });
});
