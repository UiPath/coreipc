// tslint:disable: no-unused-expression

import { expect, constructing, toJavaScript } from '@test-helpers';

import { ipc, Primitive, IIpc } from '@core';
import { Ipc } from '../../../../src/core/ipc/Ipc';
import { PublicCtor } from '../../../../src/foundation/types/reflection';
import { CancellationToken } from '../../../../src/foundation/threading/cancellation-token';

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

                @_ipc.$service.hasEndpointName('IMath')
                class Math {
                    @_ipc.$operation.hasName('Sum')
                    @_ipc.$operation.returnsPromiseOf(Primitive.number)
                    public sum(x: number, y: number): Promise<number> { throw null; }

                    @_ipc.$operation.hasName('Multiply')
                    @_ipc.$operation.returnsPromiseOf(Complex)
                    public multiply(x: Complex, y: Complex, ct?: CancellationToken): Promise<Complex> { throw null; }

                    @_ipc.$operation.returnsPromiseOf(Complex)
                    public SumComplex(x: Complex, y: Complex, ct?: CancellationToken): Promise<Complex> { throw null; }
                }

                expect(_ipc.contract.get(Math)?.endpoint).to.be.eq('IMath');

                function make(operationInfo: IIpc.OperationInfo): IIpc.OperationInfo { return operationInfo; }

                expect(_ipc.contract.get(Math)?.operations.get('sum')).to.deep.include(
                    make({
                        methodName: 'sum',
                        operationName: 'Sum',
                        hasEndingCancellationToken: false,
                        returnType: Promise,
                        parameterTypes: [Number, Number],
                        returnsPromiseOf: Primitive.number,
                    }));

                expect(_ipc.contract.get(Math)?.operations.get('multiply')).to.deep.include(
                    make({
                        methodName: 'multiply',
                        operationName: 'Multiply',
                        hasEndingCancellationToken: true,
                        returnType: Promise,
                        parameterTypes: [Complex, Complex, CancellationToken as any],
                        returnsPromiseOf: Complex,
                    }));

                const actual = _ipc.contract.get(Math)?.operations.get('SumComplex');
                expect(actual).to.deep.include(
                    make({
                        methodName: 'SumComplex',
                        operationName: 'SumComplex',
                        hasEndingCancellationToken: true,
                        returnType: Promise,
                        parameterTypes: [Complex, Complex, CancellationToken as any],
                        returnsPromiseOf: Complex,
                    }));
            });
        });
    });
});
