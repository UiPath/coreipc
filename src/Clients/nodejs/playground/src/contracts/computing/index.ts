import { ipc, CancellationToken } from '@uipath/coreipc';

export class ComplexNumber {

    constructor(public A: number, public B: number) { }

}

@ipc.$service
export class IComputingService {

    @ipc.$operation
    public AddComplexNumber(x: ComplexNumber, y: ComplexNumber, ct: CancellationToken): Promise<ComplexNumber> { throw void 0; }

}

