import { IPipeWrapper, PipeSignal, PipeDataSignal, PipeClosedSignal } from '../../src/internals/pipe-wrapper';
import { ReplaySubject } from 'rxjs';
import { ChannelReader } from '../../src/internals/channel-reader';
import { CancellationToken, PromiseHelper, CancellationTokenSource, EndOfStreamError } from '@uipath/ipc-helpers';

function range(start: number, length: number): number[] {
    const array = new Array<number>();
    const lastExclusive = start + length;
    for (let v = start; v < lastExclusive; v++) {
        array.push(v);
    }
    return array;
}

describe('ChannelReader', () => {
    test('ctor-doesnt-throw', () => {
        const mockPipe: IPipeWrapper = {
            signals: new ReplaySubject<PipeSignal>(),
            write: jest.fn(),
            dispose: jest.fn()
        };
        const channelReader = new ChannelReader(mockPipe);
    });
    test('dispose-doesnt-throw', () => {
        const mockPipe: IPipeWrapper = {
            signals: new ReplaySubject<PipeSignal>(),
            write: jest.fn(),
            dispose: jest.fn()
        };
        const channelReader = new ChannelReader(mockPipe);
        channelReader.dispose();
    });
    test('readBufferAsync-works', async () => {
        const subject = new ReplaySubject<PipeSignal>();
        const mockPipe: IPipeWrapper = {
            signals: subject,
            write: jest.fn(),
            dispose: jest.fn()
        };
        const channelReader = new ChannelReader(mockPipe);

        subject.next(new PipeDataSignal(Buffer.from(range(0, 120))));

        const buffer = Buffer.alloc(100);
        const promise1 = channelReader.readBufferAsync(buffer, CancellationToken.default);
        await expect(promise1).resolves.toBeUndefined();

        const promise2 = channelReader.readBufferAsync(buffer, CancellationToken.default);
        const mockThen = jest.fn();
        const mockCatch = jest.fn();
        promise2.then(mockThen, mockCatch);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        subject.next(new PipeDataSignal(Buffer.from(range(120, 70))));

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        subject.next(new PipeDataSignal(Buffer.from(range(190, 40))));

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen).toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        expect(buffer).toEqual(Buffer.from(range(100, 100)));

        subject.next(new PipeClosedSignal(null));

        const promise3 = channelReader.readBufferAsync(buffer, CancellationToken.default);
        await expect(promise3).rejects.toEqual(new EndOfStreamError());
    });
    test('readBufferAsync-cancels', async () => {
        const subject = new ReplaySubject<PipeSignal>();
        const mockPipe: IPipeWrapper = {
            signals: subject,
            write: jest.fn(),
            dispose: jest.fn()
        };
        const channelReader = new ChannelReader(mockPipe);

        const destination = Buffer.alloc(100);
        const cts = new CancellationTokenSource();

        const promise = channelReader.readBufferAsync(destination, cts.token);

        const mockThen = jest.fn();
        const mockCatch = jest.fn();
        promise.then(mockThen, mockCatch);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        cts.cancel();

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).toHaveBeenCalled();

        await expect(promise).rejects.toEqual(new Error('Task was canceled'));
    });
    test('readBufferAsync-throws-EndOfStream', async () => {
        const subject = new ReplaySubject<PipeSignal>();
        const mockPipe: IPipeWrapper = {
            signals: subject,
            write: jest.fn(),
            dispose: jest.fn()
        };
        const channelReader = new ChannelReader(mockPipe);

        const destination = Buffer.alloc(100);

        const promise = channelReader.readBufferAsync(destination, CancellationToken.default);

        const mockThen = jest.fn();
        const mockCatch = jest.fn();

        promise.then(mockThen, mockCatch);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        subject.next(new PipeDataSignal(Buffer.from(range(0, 50))));

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        const errorMessage = 'this is a test';
        subject.next(new PipeClosedSignal(new Error(errorMessage)));

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).toHaveBeenCalled();

        await expect(promise).rejects.toEqual(new EndOfStreamError(new Error(errorMessage)));
    });
    test('readBufferAsync-returns-immediately-for-0-length-destination', async () => {
        const subject = new ReplaySubject<PipeSignal>();
        const mockPipe: IPipeWrapper = {
            signals: subject,
            write: jest.fn(),
            dispose: jest.fn()
        };
        const channelReader = new ChannelReader(mockPipe);

        const destination = Buffer.alloc(0);

        const promise = channelReader.readBufferAsync(destination, CancellationToken.default);

        const mockThen = jest.fn();
        const mockCatch = jest.fn();

        promise.then(mockThen, mockCatch);

        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen).toBeCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await expect(promise).resolves.toBeUndefined();
    });
    test('readBufferAsync-throws-if-called-twice-concurrently', async () => {
        const subject = new ReplaySubject<PipeSignal>();
        const mockPipe: IPipeWrapper = {
            signals: subject,
            write: jest.fn(),
            dispose: jest.fn()
        };
        const channelReader = new ChannelReader(mockPipe);

        const promise1 = channelReader.readBufferAsync(Buffer.alloc(100), CancellationToken.default);
        const promise2 = channelReader.readBufferAsync(Buffer.alloc(100), CancellationToken.default);

        const mockThen1 = jest.fn();
        const mockCatch1 = jest.fn();
        const mockThen2 = jest.fn();
        const mockCatch2 = jest.fn();

        promise1.then(mockThen1, mockCatch1);
        promise2.then(mockThen2, mockCatch2);

        expect(mockThen1).not.toHaveBeenCalled();
        expect(mockCatch1).not.toHaveBeenCalled();
        expect(mockThen2).not.toHaveBeenCalled();
        expect(mockCatch2).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockThen1).not.toHaveBeenCalled();
        expect(mockCatch1).not.toHaveBeenCalled();
        expect(mockThen2).not.toHaveBeenCalled();
        expect(mockCatch2).toHaveBeenCalled();

        await expect(promise2).rejects.toEqual(new Error('The method BufferWindow.readAsync must not be called concurrently'));
    });
});
