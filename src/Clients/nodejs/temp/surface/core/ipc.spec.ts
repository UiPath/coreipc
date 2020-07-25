import { expect } from '@test-helpers';
import { Primitive, CancellationToken } from '@foundation';
import { IIpc, Ipc } from '@core';

describe(`surface`, () => {
    describe(`ipc`, () => {
        context(`contracts`, () => {
            it(`should work`, async () => {
                const _ipc = new Ipc();

                class Complex {
                    constructor(
                        public readonly x: number,
                        public readonly y: number) { }
                }

                @_ipc.$service({ endpoint: 'IMath' })
                class IMath {
                    @_ipc.$operation({
                        name: 'Sum',
                        returnsPromiseOf: Primitive.number,
                    })
                    public sum(x: number, y: number): Promise<number> { throw null; }

                    @_ipc.$operation({
                        name: 'Multiply',
                        returnsPromiseOf: Complex,
                    })
                    public multiply(x: Complex, y: Complex, ct?: CancellationToken): Promise<Complex> { throw null; }

                    @_ipc.$operation
                    public SumComplex(x: Complex, y: Complex, ct?: CancellationToken): Promise<Complex> { throw null; }
                }

                expect(_ipc.contract.get(IMath)?.endpoint).to.be.eq('IMath');

                function make(operationInfo: IIpc.OperationInfo): IIpc.OperationInfo { return operationInfo; }

                expect(_ipc.contract.get(IMath)?.operations.get('sum')).to.deep.include(
                    make({
                        methodName: 'sum',
                        operationName: 'Sum',
                        hasEndingCancellationToken: false,
                        returnType: Promise,
                        parameterTypes: [Number, Number],
                        returnsPromiseOf: Primitive.number,
                    }));

                expect(_ipc.contract.get(IMath)?.operations.get('multiply')).to.deep.include(
                    make({
                        methodName: 'multiply',
                        operationName: 'Multiply',
                        hasEndingCancellationToken: true,
                        returnType: Promise,
                        parameterTypes: [Complex, Complex, CancellationToken as any],
                        returnsPromiseOf: Complex,
                    }));

                const actual = _ipc.contract.get(IMath)?.operations.get('SumComplex');
                expect(actual).to.deep.include(
                    make({
                        methodName: 'SumComplex',
                        operationName: 'SumComplex',
                        hasEndingCancellationToken: true,
                        returnType: Promise,
                        parameterTypes: [Complex, Complex, CancellationToken as any],
                    }));
                expect(actual).to.not.haveOwnProperty('returnsPromiseOf');
            });
        });
    });
});
