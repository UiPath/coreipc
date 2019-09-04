import { RemoteError } from '../../../src/core/surface/remote-error';
import { MockError } from '../../jest-extensions';

describe('Core-Surface-RemoteError', () => {
    const mockError = new MockError();
    const mockMethodName = 'mock-methodName';
    const mockMessage = 'mock-message';

    test(`message works`, () => {
        expect(() => { throw new RemoteError(mockError); }).toThrow(RemoteError.computeMessage(mockError));
        expect(() => { throw new RemoteError(mockError, mockMethodName); }).toThrow(RemoteError.computeMessage(mockError, mockMethodName));
        expect(() => { throw new RemoteError(mockError, mockMethodName, mockMessage); }).toThrow(RemoteError.computeMessage(mockError, mockMethodName, mockMessage));
    });
});
