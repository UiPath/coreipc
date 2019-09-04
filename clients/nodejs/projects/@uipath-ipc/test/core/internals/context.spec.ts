import '../../jest-extensions';
import { MockError } from '../../jest-extensions';

import * as Outcome from '../../../src/foundation/outcome';
import * as BrokerMessage from '../../../src/core/internals/broker/broker-message';
import { CallContext, CallbackContext, CallContextTable, ICallContext } from '../../../src/core/internals/broker/context';
import { OperationCanceledError } from '../../../src/foundation/errors/operation-canceled-error';
import { ArgumentNullError } from '../../../src/foundation/errors/argument-null-error';
import { PromisePal } from '../../../src';
import { ObjectDisposedError } from '../../../src/foundation/errors/object-disposed-error';

describe('Core-Internals-Context', () => {

    test(`CallContext works`, async () => {
        expect(() => new CallContext(undefined)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'id');
        expect(() => new CallContext(null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'id');
        expect(() => new CallContext('')).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'id');

        {
            const brokerResponse = new BrokerMessage.Response(null, null);
            const callContext = new CallContext('id-foo');
            callContext.set(new Outcome.Succeeded(brokerResponse));
            await expect(callContext.promise).resolves.toBe(brokerResponse);
        }
        {
            const error = new MockError();
            const callContext = new CallContext('id-foo');
            callContext.set(new Outcome.Faulted(error));
            await expect(callContext.promise).rejects.toBe(error);
        }
        {
            const callContext = new CallContext('id-foo');
            callContext.set(new Outcome.Canceled());
            await expect(callContext.promise).rejects.toBeInstanceOf(OperationCanceledError);
        }
    });

    test(`CallbackContext works`, async () => {
        const request = new BrokerMessage.OutboundRequest('methodName-foo', []);

        expect(() => new CallbackContext(undefined, async _ => { })).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'request');
        expect(() => new CallbackContext(null, async _ => { })).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === 'request');

        expect(() => new CallbackContext(request, undefined)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === '_respondAction');
        expect(() => new CallbackContext(request, null)).toThrowInstanceOf(ArgumentNullError, error => error.maybeParamName === '_respondAction');

        expect(() => new CallbackContext(request, async _ => { })).not.toThrow();

        const mockAction = jest.fn(() => PromisePal.completedPromise);
        const callbackContext = new CallbackContext(request, mockAction);
        const response = new BrokerMessage.Response(null, null);

        await expect(callbackContext.respondAsync(response)).resolves.toBeUndefined();
        expect(mockAction).toHaveBeenCalledTimes(1);
        expect(mockAction).toBeCalledWith(response);
    });

    test(`CallContextTable.ctor doesn't throw`, () => {
        expect(() => new CallContextTable()).not.toThrow();
    });

    test(`CallContextTable.createContext works`, async () => {
        const table = new CallContextTable();

        let createdContext: ICallContext;
        expect(() => createdContext = table.createContext()).not.toThrow();
        expect(createdContext).toBeTruthy();
        expect(createdContext.id).toBeTruthy();
        expect(createdContext.promise).toBeTruthy();
    });

    test(`CallContextTable.signal throws for falsy args`, () => {
        const table = new CallContextTable();
        const someOutcome = new Outcome.Succeeded(new BrokerMessage.Response(123, null));

        expect(() => table.signal(undefined, someOutcome)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'id');
        expect(() => table.signal(null, someOutcome)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'id');
        expect(() => table.signal('', someOutcome)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'id');

        expect(() => table.signal('foo', undefined)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'outcome');
        expect(() => table.signal('foo', null)).toThrowInstanceOf(ArgumentNullError, x => x.maybeParamName === 'outcome');
    });

    test(`CallContextTable.signal works`, async () => {
        const someBrokerResponse = new BrokerMessage.Response(123, null);
        const someOutcome = new Outcome.Succeeded(someBrokerResponse);

        const table = new CallContextTable();
        expect(() => table.signal('inexistent-id', someOutcome)).not.toThrow();

        const context = table.createContext();

        expect(() => table.signal(context.id, someOutcome)).not.toThrow();
        await expect(context.promise).resolves.toBe(someBrokerResponse);
    });

    test(`CallContextTable.dispose works, is idempotent and cancels all contexts`, async () => {
        const table = new CallContextTable();

        const context1 = table.createContext();
        const context2 = table.createContext();
        const context3 = table.createContext();

        expect(() => table.dispose()).not.toThrow();
        expect(() => table.dispose()).not.toThrow();
        expect(() => table.dispose()).not.toThrow();

        await expect(context1.promise).rejects.toBeInstanceOf(OperationCanceledError);
        await expect(context2.promise).rejects.toBeInstanceOf(OperationCanceledError);
        await expect(context3.promise).rejects.toBeInstanceOf(OperationCanceledError);
    });

    test('CallContextTable.createContext and .signal throw if table is disposed', () => {
        const table = new CallContextTable();
        table.dispose();

        expect(() => table.createContext()).toThrowInstanceOf(ObjectDisposedError);
        expect(() => table.signal('foo', new Outcome.Canceled())).toThrowInstanceOf(ObjectDisposedError);
    });

});
