import { CallbackContext } from './callback-context';
import { InternalRequestMessage, InternalResponseMessage } from './internal-message';
import { IChannelWriter } from './channel-writer';
import { CancellationToken, PromiseHelper } from '@uipath/ipc-helpers';

describe('CallbackContext', () => {

    test('ctor-throws-when-needed', () => {
        const mockWriter: IChannelWriter = {
            writeAsync: jest.fn()
        };
        const request = new InternalRequestMessage(120, 'Foo', []);
        request.Id = '123';

        expect(() => new CallbackContext(request, CancellationToken.default, mockWriter)).not.toThrow();

        expect(() => new CallbackContext(null as any, CancellationToken.default, mockWriter)).toThrow();
        expect(() => new CallbackContext(request, CancellationToken.default, null as any)).toThrow();
        expect(() => new CallbackContext(null as any, CancellationToken.default, null as any)).toThrow();
    });

    test('respondAsync-throws-for-missing-RequestID', async () => {
        const mockWriteAsync = jest.fn();
        const mockWriter: IChannelWriter = {
            writeAsync: mockWriteAsync
        };
        const request = new InternalRequestMessage(120, 'Foo', []);

        const context = new CallbackContext(request, CancellationToken.default, mockWriter);
        const promise = context.respondAsync(new InternalResponseMessage('some data', null), CancellationToken.default);

        const mockThen = jest.fn();
        const mockCatch = jest.fn();

        promise.then(mockThen, mockCatch);

        expect(mockWriteAsync).not.toHaveBeenCalled();
        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockWriteAsync).not.toHaveBeenCalled();
        expect(mockThen).not.toHaveBeenCalled();
        expect(mockCatch).toHaveBeenCalledWith(new Error('The request must have a non-null Id'));

        await expect(promise).rejects.toEqual(new Error('The request must have a non-null Id'));
    });

    test('respondAsync-doesnt-throw-when-RequestID-is-present', async () => {
        const mockWriteAsync = jest.fn();
        const mockWriter: IChannelWriter = {
            writeAsync: mockWriteAsync
        };
        const request = new InternalRequestMessage(120, 'Foo', []);
        request.Id = '123';

        const context = new CallbackContext(request, CancellationToken.default, mockWriter);
        const promise = context.respondAsync(new InternalResponseMessage('some data', null), CancellationToken.default);

        const mockThen = jest.fn();
        const mockCatch = jest.fn();

        promise.then(mockThen, mockCatch);

        expect(mockWriteAsync).toHaveBeenCalled();
        expect(mockCatch).not.toHaveBeenCalled();
        expect(mockThen).not.toHaveBeenCalled();

        await PromiseHelper.delay(0);

        expect(mockCatch).not.toHaveBeenCalled();
        expect(mockThen).toHaveBeenCalled();

        await expect(promise).resolves.toBeUndefined();
    });

});
