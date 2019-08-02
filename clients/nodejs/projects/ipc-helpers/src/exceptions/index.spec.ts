import { EndOfStreamError } from './end-of-stream-error';
import { AggregateError } from './aggregate-error';
import { ArgumentNullError } from './argument-null-error';

describe('EndOfStreamError', () => {

    test('message', () => {
        expect(new EndOfStreamError().message).toBe('Attempted to read past the end of the stream.');
    });

});

describe('AggregateError', () => {

    test('message', () => {
        const error1 = new Error('error-1');
        const error2 = new Error('error-2');
        expect(new AggregateError([error1, error2]).message).toBe('One or more errors occurred: error-1, error-2');
    });

    test('ctor-throws-for-null-or-empty', () => {
        expect(() => new AggregateError(null as any)).toThrow();
        expect(() => new AggregateError([])).toThrow();
    });

});

describe('ArgumentNullError', () => {

    test('message', () => {
        expect(new ArgumentNullError('foo').message).toBe('Value cannot be null.\r\nParameter name: foo');
    });

});