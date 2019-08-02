import { Broker, BrokerWithCallbacks } from './broker';
import { Channel } from './channel';
import { CancellationToken, PromiseHelper, PromiseCompletionSource } from '@uipath/ipc-helpers';
import { IChannelReader } from './channel-reader';
import { MessageType, InternalRequestMessage } from './internal-message';

class MockReader implements IChannelReader {
    private _index = 0;

    constructor(private readonly _expectedBuffers: Array<Promise<Buffer>>) { }

    public async readBufferAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
        const index = this._index++;
        if (index >= this._expectedBuffers.length) {
            throw new Error('invalid test setup');
        }
        const expectedBuffer = await this._expectedBuffers[index];
        if (expectedBuffer.length !== buffer.length) {
            throw new Error('invalid test setup');
        }
        expectedBuffer.copy(buffer);
        return PromiseHelper.completedPromise;
    }
    public dispose(): void { /* */ }
}

describe('Broker', () => {

    test('ctor-throws', () => {
        expect(() => new Broker(null as any)).toThrow();
    });

    test('ctor-doesnt-throw', () => {
        const signalReceiveMessage = new PromiseCompletionSource<void>();
        const jsonPayload = JSON.stringify(new InternalRequestMessage(100, 'MyMethod', ['a', 'b', 'c']));
        const bPayload = Buffer.from(jsonPayload);

        const buffer1 = new PromiseCompletionSource<Buffer>();
        const buffer2 = new PromiseCompletionSource<Buffer>();
        const buffer3 = new PromiseCompletionSource<Buffer>();

        const mockReader = new MockReader([buffer1.promise, buffer2.promise, buffer3.promise]);

        const mockChannel = new Channel(mockReader, {
            writeAsync: jest.fn(async (buffer: Buffer, cancellationToken: CancellationToken): Promise<void> => {
                await PromiseHelper.delay(1);
            })
        });

        let broker: any = null;
        expect(() => broker = new BrokerWithCallbacks(mockChannel)).not.toThrow();

        if (broker instanceof BrokerWithCallbacks) {
            const mockHandler = jest.fn();

            const subscription = broker.callbacks.subscribe(mockHandler);

            expect(mockHandler).not.toHaveBeenCalled();

            // tslint:disable-next-line: no-angle-bracket-type-assertion
            buffer1.setResult(Buffer.from([<number> MessageType.Request]));
            buffer2.setResult(Buffer.from([bPayload.length, 0, 0, 0]));
            buffer3.setResult(Buffer.from(bPayload));

            expect(mockHandler).not.toHaveBeenCalled();

            subscription.unsubscribe();
        } else {
            throw new Error('invalid test setup');
        }
    });

});
