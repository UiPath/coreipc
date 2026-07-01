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

    public WaitForCancellation(ct: CancellationToken): Promise<boolean> {
        throw void 0;
    }

    public CancellationCount(): Promise<number> {
        throw void 0;
    }
}
