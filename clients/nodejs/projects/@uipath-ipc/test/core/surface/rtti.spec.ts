import '../../jest-extensions';
import { rtti, __hasCancellationToken__, __returns__ } from '../../../src/core/surface/rtti';

class MockClass { }
class MockContract {
    @__hasCancellationToken__
    // @ts-ignore
    public mockMethod1(): Promise<void> { throw null; }

    @__returns__(MockClass)
    // @ts-ignore
    public mockMethod2(): Promise<MockClass> { throw null; }

    public mockMethod3(): Promise<void> { throw null; }
}

describe('Core-Surface-Attributes', () => {
    test(`ClassInfo.get works`, () => {
        expect(() => rtti.ClassInfo.get(MockContract)).not.toThrow();
        expect(rtti.ClassInfo.get(MockContract)).toBeInstanceOf(rtti.ClassInfo as any);
        expect(rtti.ClassInfo.get(MockContract)).toBe(rtti.ClassInfo.get(MockContract));
    });
    test(`ClassInfo.tryGetMethod works`, () => {
        const classofMockContract = rtti.ClassInfo.get(MockContract);

        const cases: Array<{
            name: string,
            predicate: (methodInfo: rtti.MethodInfo<unknown>) => boolean
        }> = [
                { name: 'mockMethod1', predicate: methodInfo => methodInfo.method === MockContract.prototype.mockMethod1 },
                { name: 'mockMethod2', predicate: methodInfo => methodInfo.method === MockContract.prototype.mockMethod2 },
                { name: 'mockMethod3', predicate: methodInfo => methodInfo === null },
                { name: 'no-such-method', predicate: methodInfo => methodInfo === null }
            ];

        for (const _case of cases) {
            let result: rtti.MethodInfo<unknown> | null = null;
            expect(() => result = classofMockContract.tryGetMethod(_case.name)).not.toThrow();
            expect(result).toBeMatchedBy<rtti.MethodInfo<unknown>>(_case.predicate);
        }
    });
    test(`__hasCancellationToken__ works`, () => {
        const classofMockContract = rtti.ClassInfo.get(MockContract);
        expect(classofMockContract.tryGetMethod('mockMethod1').hasCancellationToken).toBe(true);
        expect(classofMockContract.tryGetMethod('mockMethod2').hasCancellationToken).toBe(false);
    });
    test(`__returns__ works`, () => {
        const classofMockContract = rtti.ClassInfo.get(MockContract);
        expect(classofMockContract.tryGetMethod('mockMethod1').maybeReturnValueCtor).toBeNull();
        expect(classofMockContract.tryGetMethod('mockMethod2').maybeReturnValueCtor).toBe(MockClass);
    });
});
