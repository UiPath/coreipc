// tslint:disable: variable-name
// tslint:disable: max-line-length
import { CancellationToken, Message, __hasCancellationToken__, __returns__ } from '@uipath/ipc';
import { TimeSpan } from '@uipath/ipc/dist/foundation/tasks/timespan';

export class SystemMessage extends Message<void> {
    constructor(public Text: string, public Delay: number, requestTimeout: TimeSpan) {
        super(requestTimeout);
    }
}
export class ComplexNumber {
    constructor(public a: number, public b: number) { }
}

export class IComputingService {
    @__hasCancellationToken__
    @__returns__(ComplexNumber)
    public AddComplexNumber(x: ComplexNumber, y: ComplexNumber, cancellationToken: CancellationToken = CancellationToken.none): Promise<ComplexNumber> { throw null; }

    @__hasCancellationToken__
    @__returns__(ComplexNumber)
    public AddComplexNumbers(numbers: ComplexNumber[], cancellationToken: CancellationToken = CancellationToken.none): Promise<ComplexNumber> { throw null; }

    @__hasCancellationToken__
    public SendMessage(message: SystemMessage, cancellationToken: CancellationToken = CancellationToken.none): Promise<string> { throw null; }
}
export class IComputingCallback {
    public GetId(): Promise<string> { throw null; }
}
