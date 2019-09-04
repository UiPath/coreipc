import '../jest-extensions';
import { AbstractMemberError } from '../../src/foundation/errors/abstract-member-error';
import { AggregateError } from '../../src/foundation/errors/aggregate-error';
import { ArgumentError } from '../../src/foundation/errors/argument-error';
import { ArgumentNullError } from '../../src/foundation/errors/argument-null-error';
import { InvalidOperationError } from '../../src/foundation/errors/invalid-operation-error';
import { ObjectDisposedError } from '../../src/foundation/errors/object-disposed-error';
import { OperationCanceledError } from '../../src/foundation/errors/operation-canceled-error';
import { TimeoutError } from '../../src/foundation/errors/timeout-error';
import { PipeBrokenError } from '../../src/foundation/errors/pipe/pipe-broken-error';
import { PipeBusyError } from '../../src/foundation/errors/pipe/pipe-busy-error';
import { PipeNotFoundError } from '../../src/foundation/errors/pipe/pipe-not-found-error';

describe('Foundation-Errors', () => {
    const str1 = 'string #1';
    const str2 = 'string #2';

    test('AbstractMemberError', () => {
        expect(() => { throw new AbstractMemberError(); }).toThrow(AbstractMemberError.computeMessage());
        expect(() => { throw new AbstractMemberError(str1); }).toThrow(AbstractMemberError.computeMessage(str1));
        expect(() => { throw new AbstractMemberError(str1, str2); }).toThrow(AbstractMemberError.computeMessage(str1, str2));
    });
    test('AggregateError', () => {
        const error1 = new Error();

        expect(() => { throw new AggregateError(); }).toThrow(AggregateError.defaultMessage);
        expect(() => { throw new AggregateError(str1); }).toThrowErrorWhich<AggregateError>(thrown => thrown.errors.length === 0);
        expect(() => { throw new AggregateError(error1); }).toThrowErrorWhich<AggregateError>(thrown => thrown.errors.length === 1 && thrown.errors[0] === error1);
    });
    test('ArgumentError', () => {
        expect(() => { throw new ArgumentError(); }).toThrow(ArgumentError.defaultMessage);
        expect(() => { throw new ArgumentError(str1); }).toThrow(ArgumentError.computeMessage(str1));
        expect(() => { throw new ArgumentError(str1, str2); }).toThrow(ArgumentError.computeMessage(str1, str2));
    });
    test('ArgumentNullError', () => {
        expect(() => { throw new ArgumentNullError(); }).toThrow(ArgumentNullError.defaultMessage);
        expect(() => { throw new ArgumentNullError(str1); }).toThrow(ArgumentNullError.computeMessage(str1));
        expect(() => { throw new ArgumentNullError(str1, str2); }).toThrow(ArgumentNullError.computeMessage(str1, str2));
    });
    test('InvalidOperationError', () => {
        expect(() => { throw new InvalidOperationError(); }).toThrow(InvalidOperationError.defaultMessage);
        expect(() => { throw new InvalidOperationError(str1); }).toThrow(str1);
    });
    test('ObjectDisposedError', () => {
        expect(() => { throw new ObjectDisposedError(str1); }).toThrow(ObjectDisposedError.computeMessage(str1));
        expect(() => { throw new ObjectDisposedError(str1, str2); }).toThrow(ObjectDisposedError.computeMessage(str1, str2));
    });
    test('OperationCanceledError', () => {
        expect(() => { throw new OperationCanceledError(); }).toThrow(OperationCanceledError.defaultMessage);
        expect(() => { throw new OperationCanceledError(str1); }).toThrow(str1);
    });
    test('TimeoutError', () => {
        expect(() => { throw new TimeoutError(); }).toThrow(TimeoutError.defaultMessage);
        expect(() => { throw new TimeoutError(str1); }).toThrow(str1);
    });

    test('PipeBrokenError', () => {
        expect(() => { throw new PipeBrokenError(); }).toThrow(PipeBrokenError.defaultMessage);
        expect(() => { throw new PipeBrokenError(str1); }).toThrow(str1);
    });
    test('PipeBusyError', () => {
        expect(() => { throw new PipeBusyError(); }).toThrow(PipeBusyError.defaultMessage);
        expect(() => { throw new PipeBusyError(str1); }).toThrow(str1);
    });
    test('PipeNotFoundError', () => {
        expect(() => { throw new PipeNotFoundError(); }).toThrow(PipeNotFoundError.defaultMessage);
        expect(() => { throw new PipeNotFoundError(str1); }).toThrow(str1);
    });

});
