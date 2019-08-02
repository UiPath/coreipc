import { IPipeWrapper, PipeSignal } from './pipe-wrapper';
import { Observable, Subject } from 'rxjs';
import { ChannelWriter } from './channel-writer';
import { CancellationTokenSource, PromiseHelper } from '@uipath/ipc-helpers';

class MockPipeWrapper implements IPipeWrapper {
    private readonly _signals = new Subject<PipeSignal>();
    public get signals(): Observable<PipeSignal> { return this._signals; }

    private _maybeCallback: (() => void) | null = null;

    // tslint:disable-next-line: no-empty
    public write(data: Buffer, callback: () => void): void {
        this._maybeCallback = callback;
    }
    public invokeCallback(): void {
        if (this._maybeCallback) {
            this._maybeCallback();
        } else {
            throw new Error('Callback was not set');
        }
    }
}

describe('ChannelWriter', () => {

    test('ctor-doesnt-throw', () => {
        expect(() => new ChannelWriter({} as any)).not.toThrow();
    });
    test('ctor-throws', () => {
        expect(() => new ChannelWriter(null as any)).toThrow();
    });
    test('ct-works', async () => {
        const mockPipe = new MockPipeWrapper();
        const mockThen = jest.fn();
        const mockElse = jest.fn();

        const writer = new ChannelWriter(mockPipe);
        const buffer = Buffer.from([0, 1, 2, 3]);
        const cts = new CancellationTokenSource();

        const promise = writer.writeAsync(buffer, cts.token);
        promise.then(mockThen, mockElse);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockElse).not.toHaveBeenCalled();

        await PromiseHelper.yield();

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockElse).not.toHaveBeenCalled();

        cts.cancel();

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockElse).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockElse).toHaveBeenCalled();
    });
    test('ct-works-2', async () => {
        const mockPipe = new MockPipeWrapper();
        const mockThen = jest.fn();
        const mockElse = jest.fn();

        const writer = new ChannelWriter(mockPipe);
        const buffer = Buffer.from([0, 1, 2, 3]);
        const cts = new CancellationTokenSource();

        const promise = writer.writeAsync(buffer, cts.token);
        promise.then(mockThen, mockElse);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockElse).not.toHaveBeenCalled();

        await PromiseHelper.yield();

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockElse).not.toHaveBeenCalled();

        mockPipe.invokeCallback();

        await PromiseHelper.delay(0);
        expect(() => cts.cancel()).not.toThrow();

        expect(mockThen).toHaveBeenCalled();
        expect(mockElse).not.toHaveBeenCalled();
    });

});
