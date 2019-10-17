// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import { StreamWrapper } from '@core/internals/stream-wrapper';
import { Message } from '@core/surface';

import * as BrokerMessage from '@core/internals/broker-message';
import * as WireMessage from '@core/internals/wire-message';

import { CancellationTokenSource, CancellationToken, TimeSpan } from '@foundation/threading';
import { ArgumentNullError, ArgumentError, InvalidOperationError } from '@foundation/errors';
import { IDisposable } from '@foundation/disposable';
import { IPipeClientStream } from '@foundation/pipes';

use(spies);

describe(`core:internals -> class:StreamWrapper`, () => {
    class MockPipeClientStream implements IPipeClientStream {
        public async writeAsync(buffer: Buffer, cancellationToken: CancellationToken): Promise<void> {
        }

        public async readAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<void> {
        }

        public async disposeAsync(): Promise<void> {
        }
    }

    context(`ctor`, () => {
        it(`should throw provided a falsy stream`, () => {
            (() => new StreamWrapper(null as any)).should.throw(ArgumentNullError).with.property('maybeParamName', 'stream');
            (() => new StreamWrapper(undefined as any)).should.throw(ArgumentNullError).with.property('maybeParamName', 'stream');
        });

        it(`shouldn't throw provided a truthy stream`, () => {
            (() => new StreamWrapper({} as any)).should.not.throw();
        });
    });

    context(`property:messages`, () => {
        it(`shouldn't be null or undefined`, () => {
            expect(new StreamWrapper({} as any).messages).not.to.be.null.and.not.to.be.undefined;
        });
    });

    context(`property:isConnected`, () => {
        it(`should be true at 1st`, () => {
            new StreamWrapper({} as any).isConnected.should.be.true;
        });
    });

    context(`method:disposeAsync`, () => {
        it(`shouldn't throw even when called multiple times`, async () => {
            const mock = new MockPipeClientStream();
            const wrapper = new StreamWrapper(mock);

            await wrapper.disposeAsync().should.eventually.not.be.rejected;
            await wrapper.disposeAsync().should.eventually.not.be.rejected;
        });

        it(`should cause the completion of the Observable returned by the messages property`, async () => {
            const mock = new MockPipeClientStream();
            const wrapper = new StreamWrapper(mock);
            const messagesObserver = {
                complete: spy(() => { })
            };
            wrapper.messages.subscribe(messagesObserver);

            await wrapper.disposeAsync();
            messagesObserver.complete.should.have.been.called;
        });
    });
});
