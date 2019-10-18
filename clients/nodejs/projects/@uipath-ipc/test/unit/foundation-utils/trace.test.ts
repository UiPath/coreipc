// tslint:disable: no-unused-expression

import { spy, use } from 'chai';
import spies from 'chai-spies';

import { Trace, Succeeded, Faulted, Canceled, OutcomeKind } from '@foundation/utils';
import { ArgumentNullError, OperationCanceledError } from '@foundation/errors';
import { PromiseCompletionSource } from '@foundation/threading';

use(spies);

describe(`foundation:utils -> class:Trace`, () => {
    context(`method:addListener`, () => {
        it(`should throw provided a falsy listener`, () => {
            (() => Trace.addListener(null as any)).should.throw();
            (() => Trace.addListener(undefined as any)).should.throw();
        });

        it(`shouldn't throw provided a truthy listener`, () => {
            (() => Trace.addListener((() => { }) as any)).should.not.throw();
        });

        it(`should return an IDisposable`, () => {
            Trace.addListener(() => { }).
                should.be.instanceOf(Object).
                with.property('dispose').
                which.is.instanceOf(Function);
        });

        it(`should register the listener (it will get called when calling Trace.log)`, () => {
            const listenerSpy = spy(() => { });
            Trace.addListener(listenerSpy);
            Trace.log('foo');
            listenerSpy.should.have.been.called.once.with('foo');

            Trace.log('foo');
            listenerSpy.should.have.been.called.twice.with('foo');
        });

        it(`should return an IDisposable whose dispose method doesn't throw when called`, () => {
            const listenerSpy = spy(() => { });

            const disposable = Trace.addListener(listenerSpy);
            (() => disposable.dispose()).should.not.throw();
        });

        it(`should return an IDisposable that unregisters the listener when calling its dispose method`, () => {
            const listenerSpy = spy(() => { });

            Trace.addListener(listenerSpy).dispose();

            Trace.log('foo');
            listenerSpy.should.not.have.been.called();
        });

        it(`should return an IDisposable that unregisters the listener and is idempotent`, () => {
            const listenerSpy = spy(() => { });

            const disposable = Trace.addListener(listenerSpy);
            disposable.dispose();
            (() => disposable.dispose()).should.not.throw();

            Trace.log('foo');
            listenerSpy.should.not.have.been.called();
        });
    });
});
