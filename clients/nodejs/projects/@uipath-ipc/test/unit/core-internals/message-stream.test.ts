// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import { PartialObserver } from 'rxjs';

import { MessageStream } from '@core/internals/message-stream';

import * as WireMessage from '@core/internals/wire-message';

import { MessageEvent } from '@core/internals/message-event';

import { CancellationToken } from '@foundation/threading';
import { ArgumentNullError } from '@foundation/errors';
import { IPipeClientStream } from '@foundation/pipes';

use(spies);

describe(`core:internals -> class:MessageStream`, () => {
    class MockPipeClientStream implements IPipeClientStream {
        constructor(private readonly _implementer?: Partial<IPipeClientStream>) {
        }

        public async writeAsync(source: Buffer, cancellationToken: CancellationToken): Promise<void> {
            if (this._implementer && this._implementer.writeAsync) {
                await this._implementer.writeAsync(source, cancellationToken);
            }
        }
        public async readAsync(destination: Buffer, cancellationToken: CancellationToken): Promise<void> {
            if (this._implementer && this._implementer.readAsync) {
                await this._implementer.readAsync(destination, cancellationToken);
            }
        }
        public async disposeAsync(): Promise<void> {
            if (this._implementer && this._implementer.disposeAsync) {
                await this._implementer.disposeAsync();
            }
        }
    }

    class SourcePipeClientStream extends MockPipeClientStream {
        constructor(source: Buffer) {
            super({
                async readAsync(destination: Buffer, cancellationToken: CancellationToken) {
                    source.length.should.be.gte(destination.length);

                    source.copy(destination);
                    source = source.subarray(destination.length);
                }
            });
        }
    }

    context(`ctor`, () => {
        it(`should throw provided a falsy stream`, () => {
            (() => new MessageStream(null as any)).should.throw(ArgumentNullError).with.property('paramName', 'stream');
            (() => new MessageStream(undefined as any)).should.throw(ArgumentNullError).with.property('paramName', 'stream');
        });

        it(`shouldn't throw provided a truthy stream`, () => {
            (() => new MessageStream({} as any)).should.not.throw();
        });
    });

    context(`property:messages`, () => {
        it(`shouldn't be null or undefined`, () => {
            expect(new MessageStream({} as any).messages).not.to.be.null.and.not.to.be.undefined;
        });

        it(`should emit when there's data avaible`, async () => {
            const sourceObj = new WireMessage.Request(1, 'id', 'methodName', ['1', 'true', 'null']);
            const sourceJson = JSON.stringify(sourceObj);
            const sourceBufferPayload = Buffer.from(sourceJson, 'utf-8');
            const sourceBufferCb = Buffer.alloc(4);
            sourceBufferCb.writeUInt32LE(sourceBufferPayload.length, 0);

            const sourceBuffer = Buffer.concat([
                Buffer.of(WireMessage.Type.Request as number), // Type = Request
                sourceBufferCb,
                sourceBufferPayload
            ]);
            const sourceStream = new SourcePipeClientStream(sourceBuffer);

            let messageStream: MessageStream = null as any;
            try {
                messageStream = new MessageStream(sourceStream);
                const messagesObserver: PartialObserver<MessageEvent> = {
                    next: spy((value: MessageEvent) => {
                        expect(value).not.to.be.null.and.not.to.be.undefined;
                        expect(value.messageStream).to.equal(messageStream);
                        expect(value.message).not.to.be.null.and.not.to.be.undefined;
                        value.message.should.be.instanceOf(WireMessage.Request);
                        const request = value.message as WireMessage.Request;
                        expect(request.Id).to.be.equal('id');
                        expect(request.MethodName).to.be.equal('methodName');
                        expect(request.TimeoutInSeconds).to.be.equal(1);
                        expect(request.Parameters).to.be.deep.equal(['1', 'true', 'null']);
                    })
                };
                messageStream.messages.subscribe(messagesObserver as any);

                await Promise.yield();

                messagesObserver.next.should.have.been.called();
            } finally {
                if (messageStream) {
                    await messageStream.disposeAsync();
                }
            }
        });
    });

    context(`property:isConnected`, () => {
        it(`should be true at 1st`, () => {
            new MessageStream({} as any).isConnected.should.be.true;
        });
    });

    context(`method:writeAsync`, () => {
        it(`should call the underlying IPipeClientStream's writeAsync method`, () => {
            const mock = new MockPipeClientStream();
            (mock as any).writeAsync = spy(() => { });

            const messageStream = new MessageStream(mock);
            messageStream.writeAsync(Buffer.of(0, 1, 2, 3), CancellationToken.none);
            mock.writeAsync.should.have.been.called();
        });
    });

    context(`method:disposeAsync`, () => {
        it(`shouldn't throw even when called multiple times`, async () => {
            const mockPipeClientStream = new MockPipeClientStream();
            const mockMessageStream = new MessageStream(mockPipeClientStream);

            await mockMessageStream.disposeAsync().should.eventually.not.be.rejected;
            await mockMessageStream.disposeAsync().should.eventually.not.be.rejected;
        });

        it(`should cause the completion of the Observable returned by the messages property`, async () => {
            const mockPipeClientStream = new MockPipeClientStream();
            const mockMessageStream = new MessageStream(mockPipeClientStream);

            const observer = {
                complete: spy(() => { })
            };
            mockMessageStream.messages.subscribe(observer);

            await mockMessageStream.disposeAsync();

            expect(observer.complete).to.have.been.called();
        });
    });
});
