import { Observable, Subject } from 'rxjs';
import { constructing, toJavaScript, spy, forInstance } from '@test-helpers';
import {
    CancellationToken,
    ArgumentNullError,
    ArgumentOutOfRangeError,
    ArgumentError,
    InvalidOperationError,
    EndOfStreamError,
    TimeSpan,
    SocketStream,
    Socket,
} from '@foundation';

describe(`internals`, () => {
    describe(`SocketStream`, () => {
        context(`the constructor`, () => {
            it(`should not throw when called with valid args`, () => {
                class MockSocket extends Socket {
                    private readonly _data = new Subject<Buffer>();

                    public get $data(): Observable<Buffer> { return this._data; }
                    public write(buffer: Buffer, ct: CancellationToken): Promise<void> {
                        throw new Error('Method not implemented.');
                    }
                    public dispose(): void { }
                }

                constructing(SocketStream, new MockSocket()).should.not.throw();
            });

            context(`should throw when called with a falsy socket`, () => {
                for (const falsyArg of [null, undefined] as never[]) {
                    it(`new SocketStream(${toJavaScript(falsyArg)}) should throw ArgumentNullError`, () => {
                        constructing(SocketStream, falsyArg)
                            .should.throw(ArgumentNullError)
                            .with.property('paramName', 'socket');
                    });
                }
            });

            context(`should throw when called with something other than a Socket`, () => {
                for (const invalidArg of [123, 'some string', true, () => { }, Symbol(), {}] as never[]) {
                    it(`new SocketStream(${toJavaScript(invalidArg)}) should throw ArgumentOutOfRangeError`, () => {
                        constructing(SocketStream, invalidArg)
                            .should.throw(ArgumentOutOfRangeError)
                            .with.property('paramName', 'socket');
                    });
                }
            });
        });

        context(`the write method`, () => {
            class MockSocket extends Socket {
                private readonly _data = new Subject<Buffer>();
                public get $data(): Observable<Buffer> { return this._data; }
                public async write(buffer: Buffer, ct: CancellationToken): Promise<void> { }
                public dispose(): void { }
            }

            it(`should not throw`, async () => {
                const socket = new MockSocket();
                socket.write = spy(async (buffer: Buffer, ct: CancellationToken) => { });
                const stream = new SocketStream(socket);
                await stream.write(Buffer.alloc(10), CancellationToken.none).should.eventually.be.fulfilled;
                socket.write.should.have.been.called();
            });

            it(`should not call the underlying Socket's write method when the provided Buffer is empty`, async () => {
                const socket = new MockSocket();
                socket.write = spy(async (buffer: Buffer, ct: CancellationToken) => { });
                const stream = new SocketStream(socket);
                await stream.write(Buffer.alloc(0), CancellationToken.none);
                socket.write.should.not.have.been.called();
            });

            for (const _case of [
                { expectedError: ArgumentNullError, paramName: 'buffer', args: [] },
                { expectedError: ArgumentNullError, paramName: 'buffer', args: [null] },
                { expectedError: ArgumentNullError, paramName: 'buffer', args: [undefined] },
                { expectedError: ArgumentOutOfRangeError, paramName: 'buffer', args: [123] },
                { expectedError: ArgumentOutOfRangeError, paramName: 'buffer', args: ['some string'] },
                { expectedError: ArgumentOutOfRangeError, paramName: 'buffer', args: [() => { }] },
                { expectedError: ArgumentNullError, paramName: 'ct', args: [Buffer.alloc(1)] },
                { expectedError: ArgumentNullError, paramName: 'ct', args: [Buffer.alloc(1), null] },
                { expectedError: ArgumentNullError, paramName: 'ct', args: [Buffer.alloc(1), undefined] },
                { expectedError: ArgumentOutOfRangeError, paramName: 'ct', args: [Buffer.alloc(1), 123] },
                { expectedError: ArgumentOutOfRangeError, paramName: 'ct', args: [Buffer.alloc(1), 'some string'] },
                { expectedError: ArgumentOutOfRangeError, paramName: 'ct', args: [Buffer.alloc(1), () => { }] },
            ]) {
                it(`write(${(_case.args as never[]).map(toJavaScript).join(', ')}) should throw ${_case.expectedError.name} with 'paramName' === '${_case.paramName}'`, () => {
                    const socket = new MockSocket();
                    const stream = new SocketStream(socket);
                    stream.write(...(_case.args as any as [Buffer, CancellationToken]))
                        .should.eventually.be.rejectedWith(_case.expectedError)
                        .with.property('paramName', _case.paramName);
                });
            }
        });

        context(`the read method`, () => {
            it(`should not throw`, async () => {
                class MockSocket extends Socket {
                    constructor(private readonly _data: Subject<Buffer>) { super(); }
                    public get $data(): Observable<Buffer> { return this._data; }
                    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> { }
                    public dispose(): void { }
                }

                const subject = new Subject<Buffer>();
                const stream = new SocketStream(new MockSocket(subject));
                const destination = Buffer.alloc(10);
                subject.next(Buffer.alloc(5));
                await stream.read(destination, 0, destination.length, CancellationToken.none)
                    .should.eventually.be.fulfilled
                    .and.satisfy((x: number) => x === 5);
            });

            context(`should throw ArgumentNullError when buffer is falsy`, () => {
                for (const argBuffer of [null, undefined] as never[]) {
                    for (const argOffset of [0, 1, 2] as never[]) {
                        for (const argLength of [0, 1, 2] as never[]) {
                            const args = [argBuffer, argOffset, argLength, CancellationToken.none] as [Buffer, number, number, CancellationToken];
                            const strArgs = args.map(toJavaScript).join(', ');

                            it(`read(${strArgs}) should throw ArgumentNullError`, async () => {
                                class MockSocket extends Socket {
                                    public get $data(): Observable<Buffer> { return new Subject<Buffer>(); }
                                    public write(buffer: Buffer, ct: CancellationToken): Promise<void> { throw new Error('Method not implemented.'); }
                                    public dispose(): void { }
                                }

                                const stream = new SocketStream(new MockSocket());
                                await stream.read(...args)
                                    .should.eventually.be.rejectedWith(ArgumentNullError)
                                    .with.property('paramName', 'buffer');
                            });
                        }
                    }
                }
            });

            context(`should throw ArgumentOutOfRangeError when buffer is not a Buffer`, () => {
                for (const argBuffer of [123, 'some string', true, () => { }, {}, Symbol()] as never[]) {
                    for (const argOffset of [0, 1, 2] as never[]) {
                        for (const argLength of [0, 1, 2] as never[]) {
                            const args = [argBuffer, argOffset, argLength, CancellationToken.none] as [Buffer, number, number, CancellationToken];
                            const strArgs = args.map(toJavaScript).join(', ');

                            it(`read(${strArgs}) should throw ArgumentNullError`, async () => {
                                class MockSocket extends Socket {
                                    public get $data(): Observable<Buffer> { return new Subject<Buffer>(); }
                                    public write(buffer: Buffer, ct: CancellationToken): Promise<void> { throw new Error('Method not implemented.'); }
                                    public dispose(): void { }
                                }

                                const stream = new SocketStream(new MockSocket());
                                await stream.read(...args)
                                    .should.eventually.be.rejectedWith(ArgumentOutOfRangeError)
                                    .with.property('paramName', 'buffer');
                            });
                        }
                    }
                }
            });

            context(`should throw ArgumentNullError when ct is falsy`, () => {
                const argBuffer = Buffer.alloc(10);
                for (const argCt of [null, undefined] as never[]) {
                    for (const argOffset of [0, 1, 2] as never[]) {
                        for (const argLength of [0, 1, 2] as never[]) {
                            const args = [argBuffer, argOffset, argLength, argCt] as [Buffer, number, number, CancellationToken];
                            const strArgs = args.map(toJavaScript).join(', ');

                            it(`read(${strArgs}) should throw ArgumentNullError`, async () => {
                                class MockSocket extends Socket {
                                    public get $data(): Observable<Buffer> { return new Subject<Buffer>(); }
                                    public write(buffer: Buffer, ct: CancellationToken): Promise<void> { throw new Error('Method not implemented.'); }
                                    public dispose(): void { }
                                }

                                const stream = new SocketStream(new MockSocket());
                                await stream.read(...args)
                                    .should.eventually.be.rejectedWith(ArgumentNullError)
                                    .with.property('paramName', 'ct');
                            });
                        }
                    }
                }

            });

            context(`should throw ArgumentOutOfRangeError when ct is not a CancellationToken`, () => {
                const argBuffer = Buffer.alloc(10);
                for (const argCt of [123, 'some string', true, () => { }, {}, Symbol()] as never[]) {
                    for (const argOffset of [0, 1, 2] as never[]) {
                        for (const argLength of [0, 1, 2] as never[]) {
                            const args = [argBuffer, argOffset, argLength, argCt] as [Buffer, number, number, CancellationToken];
                            const strArgs = args.map(toJavaScript).join(', ');

                            it(`read(${strArgs}) should throw ArgumentNullError`, async () => {
                                class MockSocket extends Socket {
                                    public get $data(): Observable<Buffer> { return new Subject<Buffer>(); }
                                    public write(buffer: Buffer, ct: CancellationToken): Promise<void> { throw new Error('Method not implemented.'); }
                                    public dispose(): void { }
                                }

                                const stream = new SocketStream(new MockSocket());
                                await stream.read(...args)
                                    .should.eventually.be.rejectedWith(ArgumentOutOfRangeError)
                                    .with.property('paramName', 'ct');
                            });
                        }
                    }
                }
            });

            context(`should throw ArgumentError when offset + length overflow the destination buffer`, () => {
                for (const args of [
                    [Buffer.alloc(10), 0, 11, CancellationToken.none],
                    [Buffer.alloc(10), 10, 1, CancellationToken.none],
                    [Buffer.alloc(100), 90, 20, CancellationToken.none],
                    [Buffer.alloc(100), 1, 1000, CancellationToken.none],
                    [Buffer.alloc(100), 1000, 1, CancellationToken.none],
                ] as Array<[Buffer, number, number, CancellationToken]>) {
                    const strArgs = args.map(toJavaScript).join(', ');

                    it(`read(${strArgs}) should throw ArgumentError`, async () => {
                        class MockSocket extends Socket {
                            public get $data(): Observable<Buffer> { return new Subject<Buffer>(); }
                            public write(buffer: Buffer, ct: CancellationToken): Promise<void> { throw new Error('Method not implemented.'); }
                            public dispose(): void { }
                        }

                        const stream = new SocketStream(new MockSocket());
                        await stream.read(...args)
                            .should.eventually.be.rejectedWith(
                                ArgumentError,
                                'Offset and length overflow outside the destination buffer.',
                            );
                    });
                }
            });

            it(`should throw InvalidOperationError if called twice concurrently`, async () => {
                class MockSocket extends Socket {
                    constructor(private readonly _data: Subject<Buffer>) { super(); }
                    public get $data(): Observable<Buffer> { return this._data; }
                    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> { }
                    public dispose(): void { }
                }

                const subject = new Subject<Buffer>();
                const socket = new MockSocket(subject);
                const stream = new SocketStream(socket);

                const promise1 = stream.read(Buffer.alloc(10), 0, 10, CancellationToken.none);
                await stream.read(Buffer.alloc(10), 0, 10, CancellationToken.none)
                    .should.eventually.be.rejectedWith(InvalidOperationError, 'An asynchronous read operation is already in progress.');

                subject.next(Buffer.alloc(1));
                await promise1;
            });

            it(`should throw EndOfStreamError if the underlying Socket's data observable completes while it is executing`, async () => {
                class MockSocket extends Socket {
                    constructor(private readonly _data: Subject<Buffer>) { super(); }
                    public get $data(): Observable<Buffer> { return this._data; }
                    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> { }
                    public dispose(): void { }
                }

                const subject = new Subject<Buffer>();
                const socket = new MockSocket(subject);
                const stream = new SocketStream(socket);

                const promise1 = stream.read(Buffer.alloc(10), 0, 10, CancellationToken.none);
                await Promise.yield();
                subject.complete();

                await promise1.should.eventually.be.rejectedWith(EndOfStreamError);
            });

            it(`should throw EndOfStreamError if the underlying Socket's data observable had completed before it started executing`, async () => {
                class MockSocket extends Socket {
                    constructor(private readonly _data: Subject<Buffer>) { super(); }
                    public get $data(): Observable<Buffer> { return this._data; }
                    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> { }
                    public dispose(): void { }
                }

                const subject = new Subject<Buffer>();
                const socket = new MockSocket(subject);
                const stream = new SocketStream(socket);
                subject.complete();

                await stream.read(Buffer.alloc(10), 0, 10, CancellationToken.none)
                    .should.eventually.be.rejectedWith(EndOfStreamError);
            });

            it(`should return asap if called with length === 0`, async () => {
                class MockSocket extends Socket {
                    constructor(private readonly _data: Subject<Buffer>) { super(); }
                    public get $data(): Observable<Buffer> { return this._data; }
                    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> { }
                    public dispose(): void { }
                }

                const subject = new Subject<Buffer>();
                const socket = new MockSocket(subject);
                const stream = new SocketStream(socket);

                const promise1 = Promise.delay(TimeSpan.fromMilliseconds(100));
                const promise2 = stream.read(Buffer.alloc(10), 0, 0, CancellationToken.none);

                await Promise.race([promise1, promise2])
                    .should.eventually.be.fulfilled.and.be.eq(0);
            });

            it(`should return correct data`, async () => {
                class MockSocket extends Socket {
                    constructor(private readonly _data: Subject<Buffer>) { super(); }
                    public get $data(): Observable<Buffer> { return this._data; }
                    public async write(buffer: Buffer, ct: CancellationToken): Promise<void> { }
                    public dispose(): void { }
                }

                const subject = new Subject<Buffer>();
                const socket = new MockSocket(subject);
                const stream = new SocketStream(socket);

                const destination1 = Buffer.alloc(10);
                const promise1 = stream.read(destination1, 0, destination1.length, CancellationToken.none);

                const source = Buffer.from(Array.from(Array(256).keys()));
                subject.next(source);

                await promise1.should.eventually.be.fulfilled.and.be.eq(destination1.length);
                Array.from(destination1.values())
                    .should.be.deep.eq(Array.from(Array(destination1.length).keys()));

                const destination2 = Buffer.alloc(100);
                await stream.read(destination2, 0, destination2.length, CancellationToken.none)
                    .should.eventually.be.fulfilled.and.be.deep.eq(destination2.length);
                Array.from(destination2.values())
                    .should.be.deep.eq(Array.from(Array(destination2.length).keys()).map(x => x + destination1.length));

                const destination3 = Buffer.alloc(200);
                const expectedCbRead = source.length - destination1.length - destination2.length;
                await stream.read(destination3, 0, destination3.length, CancellationToken.none)
                    .should.eventually.be.fulfilled.and.be.deep.eq(expectedCbRead);
                Array.from(destination3.values()).slice(0, expectedCbRead)
                    .should.be.deep.eq(Array.from(Array(expectedCbRead).keys()).map(x => x + destination1.length + destination2.length));
            });
        });

        context(`the dispose method`, () => {
            class MockSocket extends Socket {
                public static createSocket(subject?: Subject<Buffer>) {
                    subject = subject ?? new Subject<Buffer>();
                    return new MockSocket(subject);
                }

                public static createStream(subject?: Subject<Buffer>) {
                    subject = subject ?? new Subject<Buffer>();
                    const socket = new MockSocket(subject);
                    return new SocketStream(socket);
                }

                constructor(private readonly _data: Subject<Buffer>) { super(); }
                public get $data(): Observable<Buffer> { return this._data; }
                public async write(buffer: Buffer, ct: CancellationToken): Promise<void> { }
                public dispose(): void { }
            }

            it(`should not throw (even when called multiple times)`, () => {
                const stream = MockSocket.createStream();

                forInstance(stream).calling('dispose').should.not.throw();
                forInstance(stream).calling('dispose').should.not.throw();
            });

            it(`should call the underlying Socket's dipose method`, () => {
                const socket = MockSocket.createSocket();
                socket.dispose = spy(() => { });

                const stream = new SocketStream(socket);

                stream.dispose();
                socket.dispose.should.have.been.called();
            });
        });
    });
});
