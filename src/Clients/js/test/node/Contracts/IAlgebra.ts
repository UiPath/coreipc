import { CancellationToken, Message } from '../../../src/std';

export class IAlgebra {
    public MultiplySimple(x: number, y: number): Promise<number> {
        throw void 0;
    }

    public Sleep(milliseconds: number): Promise<boolean> {
        throw void 0;
    }

    public TestMessage(message: Message<number>): Promise<boolean> {
        throw void 0;
    }

    // Accepts a CancellationToken OR an AbortSignal interchangeably; the .NET
    // counterpart is a plain CancellationToken.
    public WaitForCancellation(cancellation: CancellationToken | AbortSignal): Promise<boolean> {
        throw void 0;
    }

    public CancellationCount(): Promise<number> {
        throw void 0;
    }

    // Asks the .NET server to invoke ICancellationCallback.Wait on us and then
    // cancel it. Resolves true once the server sees the callback observe the cancel.
    public CancelCallback(): Promise<boolean> {
        throw void 0;
    }
}
