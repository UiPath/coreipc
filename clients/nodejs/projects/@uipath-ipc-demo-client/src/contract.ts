import { CancellationToken, Message, __hasCancellationToken__, __returns__ } from '@uipath/ipc';

export class Complex {
    constructor(
        public readonly X: number,
        public readonly Y: number
    ) { }
}

export class IService {
    @__hasCancellationToken__
    @__returns__(Complex)
    public AddAsync(a: Complex, b: Message<Complex>, c: CancellationToken = CancellationToken.none): Promise<Complex> { throw null; }

    public StartTimerAsync(message: Message<void>): Promise<void> { throw null; }
}
export interface ICallback {
    AddAsync(a: number, b: number): Promise<number>;
    TimeAsync(info: string): Promise<string>;
}
