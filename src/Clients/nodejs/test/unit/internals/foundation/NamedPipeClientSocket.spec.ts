import {
    spy,
    expect,
    toJavaScript,
    MockSocketBase,
    DelayConnectMockSocket,
    forInstance,
} from '@test-helpers';

import {
    NamedPipeClientSocket,
    SocketLike,
    CancellationToken,
    CancellationTokenSource,
    TimeSpan,
    Timeout,
    ArgumentNullError,
    ArgumentOutOfRangeError,
    OperationCanceledError,
    TimeoutError,
    ObjectDisposedError,
    PromiseStatus,
} from '@foundation';

describe(`internals`, () => {
    describe(`NamedPipeClientSocket`, () => {
        class AutoConnectMockSocket extends DelayConnectMockSocket {
            constructor() { super(TimeSpan.zero); }
            public static toString() { return 'AutoConnectMockSocket'; }
        }

        context(`the connect method`, () => {
            context(`should not throw when called with valid args`, () => {
                for (const args of [
                    ['some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, AutoConnectMockSocket],
                    ['some pipe name', TimeSpan.fromMinutes(1), new CancellationTokenSource().token, AutoConnectMockSocket],
                ] as Array<[string, TimeSpan, CancellationToken, (new () => SocketLike) | undefined]>) {
                    const strArgs = args.map(toJavaScript).join(', ');

                    it(`connect(${strArgs}) should not throw`, async () => {
                        await NamedPipeClientSocket.connect(...args)
                            .should.eventually.be.fulfilled;
                    });
                }
            });

            context(`should throw when called with invalid args`, () => {
                function makeArgs(
                    pipeName?: any,
                    timeout?: any,
                    ct?: any,
                    socketLikeCtor?: any,
                ): [string, TimeSpan, CancellationToken, (new () => SocketLike) | undefined] {
                    return [pipeName, timeout, ct, socketLikeCtor] as any;
                }

                class Case {
                    private static readonly _paramNames = ['pipeName', 'timeout', 'ct', 'socketLikeCtor'];

                    constructor(
                        public readonly args: [string, TimeSpan, CancellationToken, (new () => SocketLike) | undefined],
                        public readonly error: new (...args: any[]) => any,
                        public readonly paramName: string,
                    ) { }

                    private get strArgs(): string {
                        return this.args.map((value, index) => `${Case._paramNames[index]}: ${toJavaScript(value)}`).join(', ');
                    }
                    public get testTitle(): string {
                        return `connect(${this.strArgs}) should throw ${this.error.name} { paramName: '${this.paramName}' }`;
                    }
                }

                function* enumerateCases(): Iterable<Case> {
                    yield new Case(
                        makeArgs(undefined, Timeout.infiniteTimeSpan, CancellationToken.none, AutoConnectMockSocket),
                        ArgumentNullError,
                        'pipeName');

                    yield new Case(
                        makeArgs(null, Timeout.infiniteTimeSpan, CancellationToken.none, AutoConnectMockSocket),
                        ArgumentNullError,
                        'pipeName');

                    yield new Case(
                        makeArgs('some pipe name', undefined, CancellationToken.none, AutoConnectMockSocket),
                        ArgumentNullError,
                        'timeout');

                    yield new Case(
                        makeArgs('some pipe name', null, CancellationToken.none, AutoConnectMockSocket),
                        ArgumentNullError,
                        'timeout');

                    for (const invalidValue of [123, true, Symbol(), {}]) {
                        yield new Case(
                            makeArgs(invalidValue, Timeout.infiniteTimeSpan, CancellationToken.none, AutoConnectMockSocket),
                            ArgumentOutOfRangeError,
                            'pipeName');

                        yield new Case(
                            makeArgs('some pipe name', invalidValue, CancellationToken.none, AutoConnectMockSocket),
                            ArgumentOutOfRangeError,
                            'timeout');

                        yield new Case(
                            makeArgs('some pipe name', Timeout.infiniteTimeSpan, invalidValue, AutoConnectMockSocket),
                            ArgumentOutOfRangeError,
                            'ct');

                        yield new Case(
                            makeArgs('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, invalidValue),
                            ArgumentOutOfRangeError,
                            'socketLikeCtor');
                    }
                }

                for (const _case of enumerateCases()) {
                    it(_case.testTitle, async () => {
                        await NamedPipeClientSocket.connect(..._case.args)
                            .should.eventually.rejectedWith(_case.error)
                            .with.property('paramName', _case.paramName);
                    });
                }
            });

            it(`should throw when the underlying SocketLike emits an error while connecting and should destroy the underlying SocketLike in the process`, async () => {
                const error = new Error();

                class FailConnectMockSocket extends MockSocketBase {
                    private _errorListener: ((error: Error) => void) | null = null;

                    public connect(pipeName: string, connectionListener?: (() => void) | undefined): this {
                        if (this._errorListener) { this._errorListener(error); }
                        return this;
                    }

                    public once(event: 'error', listener: (error: Error) => void): this {
                        this._errorListener = listener;
                        return this;
                    }
                    public static toString() { return 'FailConnectMockSocket'; }
                }

                FailConnectMockSocket.prototype.removeAllListeners = spy(function (this: FailConnectMockSocket, _event?: string | symbol) { return this; });
                FailConnectMockSocket.prototype.unref = spy(() => { });
                FailConnectMockSocket.prototype.destroy = spy((_error?: Error) => { });

                await NamedPipeClientSocket.connect(
                    'some pipe name',
                    Timeout.infiniteTimeSpan,
                    CancellationToken.none,
                    FailConnectMockSocket,
                )
                    .should.eventually.be.rejectedWith(Error)
                    .which.satisfies((x: any) => x === error);

                FailConnectMockSocket.prototype.removeAllListeners.should.have.been.called();
                FailConnectMockSocket.prototype.unref.should.have.been.called();
                FailConnectMockSocket.prototype.destroy.should.have.been.called();
            });

            it(`should throw when the ct is signalled while connecting is still in progress and should destroy the underlying SocketLike in the process`, async () => {
                class NeverConnectMockSocket extends DelayConnectMockSocket {
                    constructor() { super(Timeout.infiniteTimeSpan); }
                }

                NeverConnectMockSocket.prototype.removeAllListeners = spy(function (this: NeverConnectMockSocket, _event?: string | symbol) { return this; });
                NeverConnectMockSocket.prototype.unref = spy(() => { });
                NeverConnectMockSocket.prototype.destroy = spy((_error?: Error) => { });

                const cts = new CancellationTokenSource();
                const promise = NamedPipeClientSocket.connect(
                    'some pipe name',
                    Timeout.infiniteTimeSpan,
                    cts.token,
                    NeverConnectMockSocket,
                );
                const spyThen = spy((npcs: NamedPipeClientSocket) => { });
                const spyCatch = spy((error: Error) => { });
                promise.then(spyThen);
                promise.catch(spyCatch);

                await Promise.yield();
                spyThen.should.not.have.been.called();
                spyCatch.should.not.have.been.called();

                cts.cancel();
                await Promise.yield();

                spyCatch.should.have.been.called();
                await promise.should.eventually.be.rejectedWith(OperationCanceledError);
                NeverConnectMockSocket.prototype.removeAllListeners.should.have.been.called();
                NeverConnectMockSocket.prototype.unref.should.have.been.called();
                NeverConnectMockSocket.prototype.destroy.should.have.been.called();
            });

            it(`should throw when the timeout is reached while connecting is still in progress and should destroy the underlying SocketLike in the process`, async () => {
                class NeverConnectMockSocket extends DelayConnectMockSocket {
                    constructor() { super(Timeout.infiniteTimeSpan); }
                }

                NeverConnectMockSocket.prototype.removeAllListeners = spy(function (this: NeverConnectMockSocket, _event?: string | symbol) { return this; });
                NeverConnectMockSocket.prototype.unref = spy(() => { });
                NeverConnectMockSocket.prototype.destroy = spy((_error?: Error) => { });

                const spiedPromise = NamedPipeClientSocket.connect(
                    'some pipe name',
                    TimeSpan.fromMilliseconds(100),
                    CancellationToken.none,
                    NeverConnectMockSocket,
                ).spy();

                await Promise.yield();
                spiedPromise.status.should.be.eq(PromiseStatus.Running);

                await Promise.delay(TimeSpan.fromMilliseconds(100));

                spiedPromise.status.should.be.eq(PromiseStatus.Faulted);
                await spiedPromise.should.eventually.be.rejectedWith(TimeoutError);

                NeverConnectMockSocket.prototype.removeAllListeners.should.have.been.called();
                NeverConnectMockSocket.prototype.unref.should.have.been.called();
                NeverConnectMockSocket.prototype.destroy.should.have.been.called();
            });
        });

        context(`the dispose method`, () => {
            it(`should not throw even if called twice`, async () => {
                const npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, AutoConnectMockSocket);
                forInstance(npcs).calling('dispose').should.not.throw();
                forInstance(npcs).calling('dispose').should.not.throw();
            });

            it(`should destroy the underlying SocketLike`, async () => {
                class SpyMockSocket extends AutoConnectMockSocket { }
                SpyMockSocket.prototype.removeAllListeners = spy(function (this: SpyMockSocket) { return this; });
                SpyMockSocket.prototype.unref = spy(() => { });
                SpyMockSocket.prototype.destroy = spy(() => { });

                const npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, SpyMockSocket);
                forInstance(npcs).calling('dispose').should.not.throw();

                SpyMockSocket.prototype.removeAllListeners.should.have.been.called();
                SpyMockSocket.prototype.unref.should.have.been.called();
                SpyMockSocket.prototype.destroy.should.have.been.called();
            });
        });

        context(`the $data property`, () => {
            it(`should not throw`, async () => {
                let npcs: NamedPipeClientSocket | null = null;
                try {
                    npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, AutoConnectMockSocket);
                    (() => {
                        const _ = npcs?.$data;
                    }).should.not.throw();
                } finally {
                    npcs?.dispose();
                }
            });

            it(`should return an object`, async () => {
                let npcs: NamedPipeClientSocket | null = null;
                try {
                    npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, AutoConnectMockSocket);
                    expect(npcs.$data).to.be.instanceOf(Object);
                } finally {
                    npcs?.dispose();
                }
            });

            context(`the returned observable`, () => {
                it(`should emit when the underlying SocketLike emits 'data'`, async () => {
                    class DataEmittingMockSocket extends AutoConnectMockSocket {
                        private static _listener: ((data: Buffer) => void) | null = null;

                        public static send(data: Buffer): void { DataEmittingMockSocket._listener?.(data); }

                        public on(event: 'end', listener: () => void): this;
                        public on(event: 'data', listener: (data: Buffer) => void): this;
                        public on(event: 'end' | 'data', listener: (...args: any[]) => void): this {
                            switch (event) {
                                case 'data':
                                    DataEmittingMockSocket._listener = listener as any;
                                    break;
                            }
                            return this;
                        }
                    }

                    let npcs: NamedPipeClientSocket | null = null;
                    try {
                        npcs = await NamedPipeClientSocket.connect(
                            'some pipe',
                            Timeout.infiniteTimeSpan,
                            CancellationToken.none,
                            DataEmittingMockSocket);

                        const spyNext = spy(() => { });
                        npcs.$data.subscribe(spyNext);

                        const buffer = Buffer.alloc(10);
                        DataEmittingMockSocket.send(buffer);

                        spyNext.should.have.been.called.with(buffer);
                    } finally {
                        npcs?.dispose();
                    }
                });

                it(`should complete when the NamedPipeClientSocket is disposed`, async () => {
                    let npcs: NamedPipeClientSocket | null = null;
                    try {
                        npcs = await NamedPipeClientSocket.connect(
                            'some pipe',
                            Timeout.infiniteTimeSpan,
                            CancellationToken.none,
                            AutoConnectMockSocket);

                        const spyComplete = spy(() => { });
                        npcs.$data.subscribe(undefined, undefined, spyComplete);
                        npcs.dispose();

                        spyComplete.should.have.been.called();
                    } finally {
                        npcs?.dispose();
                    }
                });

                it(`should complete when the NamedPipeClientSocket is disposed (i.e. when the underlying SocketLike emits 'end')`, async () => {
                    class EndEmittingMockSocket extends AutoConnectMockSocket {
                        private static _listener: (() => void) | null = null;

                        public static end(): void { EndEmittingMockSocket._listener?.(); }

                        public on(event: 'end', listener: () => void): this;
                        public on(event: 'data', listener: (data: Buffer) => void): this;
                        public on(event: 'end' | 'data', listener: (...args: any[]) => void): this {
                            switch (event) {
                                case 'end':
                                    EndEmittingMockSocket._listener = listener as any;
                                    break;
                            }
                            return this;
                        }
                    }

                    let npcs: NamedPipeClientSocket | null = null;
                    try {
                        npcs = await NamedPipeClientSocket.connect(
                            'some pipe',
                            Timeout.infiniteTimeSpan,
                            CancellationToken.none,
                            EndEmittingMockSocket);

                        const spyComplete = spy(() => { });
                        npcs.$data.subscribe(undefined, undefined, spyComplete);

                        EndEmittingMockSocket.end();
                        spyComplete.should.have.been.called();
                    } finally {
                        npcs?.dispose();
                    }
                });
            });
        });

        context(`the write method`, () => {
            class AutoWriteMockSocket extends AutoConnectMockSocket {
                public write(_buffer: string | Uint8Array, cb?: (err?: Error) => void): boolean {
                    if (cb) { cb(); }
                    return true;
                }
            }

            it(`should not throw when called with valid args`, async () => {
                let npcs: NamedPipeClientSocket | null = null;
                try {
                    npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, AutoWriteMockSocket);
                    await npcs.write(Buffer.alloc(10), CancellationToken.none)
                        .should.eventually.be.fulfilled;
                } finally {
                    npcs?.dispose();
                }
            });

            context(`should throw when called with invalid args`, () => {
                class Case {
                    constructor(
                        public readonly error: new (...args: any[]) => Error,
                        public readonly paramName: string,
                        ...args: any[]) { this.args = [...args] as any; }

                    public readonly args: [Buffer, CancellationToken];
                    private get strArgs(): string { return this.args.map(toJavaScript).join(', '); }
                    public get testTitle(): string { return `write(${this.strArgs}) should throw ${this.error.name} { paramName: '${this.paramName}' }`; }
                }

                function* enumerateCases(): Iterable<Case> {
                    yield new Case(ArgumentNullError, 'buffer');
                    yield new Case(ArgumentNullError, 'buffer', undefined);
                    yield new Case(ArgumentNullError, 'buffer', null);
                    yield new Case(ArgumentNullError, 'buffer', undefined, CancellationToken.none);
                    yield new Case(ArgumentNullError, 'ct', Buffer.alloc(10));
                    yield new Case(ArgumentNullError, 'ct', Buffer.alloc(10), undefined);
                    yield new Case(ArgumentNullError, 'ct', Buffer.alloc(10), null);
                }

                for (const _case of enumerateCases()) {
                    it(_case.testTitle, async () => {
                        let npcs: NamedPipeClientSocket | null = null;
                        try {
                            npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, AutoWriteMockSocket);
                            await npcs.write(..._case.args)
                                .should.eventually.be.rejectedWith(_case.error)
                                .with.property('paramName', _case.paramName);
                        } finally {
                            npcs?.dispose();
                        }
                    });
                }
            });

            it(`should throw when called on a disposed NamedPipeClientSocket instance`, async () => {
                const npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, AutoWriteMockSocket);
                npcs.dispose();

                await npcs.write(Buffer.alloc(10), CancellationToken.none)
                    .should.eventually.be.rejectedWith(ObjectDisposedError)
                    .with.property('objectName', 'NamedPipeClientSocket');
            });

            it(`should call the underlying SocketLike's write method`, async () => {
                class SpyMockSocket extends AutoWriteMockSocket { }
                SpyMockSocket.prototype.write = spy(AutoWriteMockSocket.prototype.write);

                let npcs: NamedPipeClientSocket | null = null;
                try {
                    npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, SpyMockSocket);
                    const promise = npcs.write(Buffer.alloc(10), CancellationToken.none);
                    await Promise.yield();
                    SpyMockSocket.prototype.write.should.have.been.called();
                    await promise.should.eventually.be.fulfilled;
                } finally {
                    npcs?.dispose();
                }
            });

            it(`should not call the underlying SocketLike's write method when called with an empty Buffer`, async () => {
                class SpyMockSocket extends AutoWriteMockSocket { }
                SpyMockSocket.prototype.write = spy(AutoWriteMockSocket.prototype.write);

                let npcs: NamedPipeClientSocket | null = null;
                try {
                    npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, SpyMockSocket);
                    await npcs.write(Buffer.alloc(0), CancellationToken.none)
                        .should.eventually.be.fulfilled;
                    SpyMockSocket.prototype.write.should.not.have.been.called();
                } finally {
                    npcs?.dispose();
                }
            });

            it(`should throw when the underlying SocketLike emits an error via the callback`, async () => {
                const error = new Error();

                class SpyMockSocket extends AutoConnectMockSocket {
                    public write(_buffer: string | Uint8Array, cb?: (err?: Error) => void): boolean {
                        if (cb) { cb(error); }
                        return true;
                    }
                }

                let npcs: NamedPipeClientSocket | null = null;
                try {
                    npcs = await NamedPipeClientSocket.connect('some pipe name', Timeout.infiniteTimeSpan, CancellationToken.none, SpyMockSocket);
                    await npcs.write(Buffer.alloc(10), CancellationToken.none)
                        .should.eventually.be.rejectedWith(error);
                } finally {
                    npcs?.dispose();
                }
            });
        });
    });
});
