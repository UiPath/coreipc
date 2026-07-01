import { Observer, Subject } from 'rxjs';

import {
    Address,
    CancellationToken,
    CancellationTokenSource,
    ConnectHelper,
    IMessageStream,
    Network,
    ObjectDisposedError,
    OperationCanceledError,
    RpcCallContext,
    RpcChannel,
    RpcMessage,
    Socket,
    Timeout,
    TimeSpan,
} from '../../../src/std';

import { expect } from 'chai';

// --- Test doubles ---

class MockSocket extends Socket {
    private readonly _data = new Subject<Buffer>();
    get $data() {
        return this._data;
    }
    async write(_buffer: Buffer, _ct: CancellationToken): Promise<void> {}
    dispose(): void {}
}

class MockAddress extends Address {
    get key() {
        return 'mock:rpc-send-cancellation';
    }
    async connect(_h: ConnectHelper, _t: TimeSpan, _ct: CancellationToken): Promise<Socket> {
        return new MockSocket();
    }
}

// A connect that the test resolves manually, to exercise a token firing while the
// very first call is still connecting (the message stream not yet created).
class DeferredMockAddress extends Address {
    public resolveConnect!: () => void;
    private readonly _connected = new Promise<Socket>((resolve) => {
        this.resolveConnect = () => resolve(new MockSocket());
    });
    get key() {
        return 'mock:rpc-send-cancellation-deferred';
    }
    async connect(_h: ConnectHelper, _t: TimeSpan, _ct: CancellationToken): Promise<Socket> {
        return this._connected;
    }
}

// Records everything the channel writes and exposes the inbound observer so the
// test can deliver a Response frame to settle an outgoing call.
class StubMessageStream implements IMessageStream {
    public readonly writes: Network.Message[] = [];
    constructor(public readonly observer: Observer<Network.Message>) {}
    async writeMessageAsync(message: Network.Message, _ct: CancellationToken): Promise<void> {
        this.writes.push(message);
    }
    async disposeAsync(): Promise<void> {}
}

class StubMessageStreamFactory implements IMessageStream.Factory {
    public last: StubMessageStream | undefined;
    create(_stream: any, observer: Observer<Network.Message>): IMessageStream {
        return (this.last = new StubMessageStream(observer));
    }
}

const tick = () => new Promise((resolve) => setTimeout(resolve, 5));

function typesOf(writes: Network.Message[]): string[] {
    return writes.map((w) => Network.Message.Type[w.type]);
}

function firstOfType(
    writes: Network.Message[],
    type: Network.Message.Type,
): Network.Message | undefined {
    return writes.find((w) => w.type === type);
}

async function setup() {
    const rpcObserver: Observer<RpcCallContext.Incomming> = {
        next() {},
        error() {},
        complete() {},
    };

    const factory = new StubMessageStreamFactory();
    const channel = RpcChannel.create(
        new MockAddress(),
        {} as any as ConnectHelper,
        TimeSpan.fromSeconds(30),
        CancellationToken.none,
        rpcObserver,
        factory,
    );

    await (channel as any)._$messageStream;

    return { channel, factory: factory! };
}

function settle(promise: Promise<RpcMessage.Response>) {
    return promise.then(
        (response) => ({ ok: true as const, response }),
        (err: unknown) => ({ ok: false as const, err }),
    );
}

function responseFrame(requestId: string): Network.Message {
    return new RpcMessage.Response(requestId, JSON.stringify(null), null).toNetwork();
}

// --- Tests ---

describe(`${RpcChannel.name} (caller sends cancel frames)`, () => {
    it('propagates a CancellationRequest frame to the peer when the token fires', async () => {
        const { channel, factory } = await setup();
        const cts = new CancellationTokenSource();

        const settled = settle(
            channel.call(new RpcMessage.Request(0, 'ISvc', 'Work', []), Timeout.infiniteTimeSpan, cts.token),
        );

        await tick();
        const requestWrite = firstOfType(factory.last!.writes, Network.Message.Type.Request);
        expect(requestWrite, `writes were ${typesOf(factory.last!.writes)}`).to.not.equal(undefined);
        const requestId = RpcMessage.Request.fromNetwork(requestWrite!).Id;

        // Nothing cancelled yet.
        expect(firstOfType(factory.last!.writes, Network.Message.Type.Cancel)).to.equal(undefined);

        cts.cancel();
        await tick();

        const cancelWrite = firstOfType(factory.last!.writes, Network.Message.Type.Cancel);
        expect(cancelWrite, `writes were ${typesOf(factory.last!.writes)}`).to.not.equal(undefined);
        expect(RpcMessage.CancellationRequest.fromNetwork(cancelWrite!).RequestId).to.equal(requestId);

        // The cancel frame must never precede its request on the wire.
        expect(typesOf(factory.last!.writes)).to.deep.equal(['Request', 'Cancel']);

        // The caller's own promise still rejects with a cancellation.
        const outcome = await settled;
        expect(outcome.ok).to.equal(false);
        expect((outcome as any).err).to.be.instanceOf(OperationCanceledError);
    });

    it('does not send a cancel frame after the call has already completed', async () => {
        const { channel, factory } = await setup();
        const cts = new CancellationTokenSource();

        const settled = settle(
            channel.call(new RpcMessage.Request(0, 'ISvc', 'Work', []), Timeout.infiniteTimeSpan, cts.token),
        );

        await tick();
        const requestId = RpcMessage.Request.fromNetwork(
            firstOfType(factory.last!.writes, Network.Message.Type.Request)!,
        ).Id;

        // Complete the call, then cancel: the (disposed) registration must not fire.
        factory.last!.observer.next(responseFrame(requestId));
        const outcome = await settled;
        expect(outcome.ok).to.equal(true);

        cts.cancel();
        await tick();

        expect(firstOfType(factory.last!.writes, Network.Message.Type.Cancel)).to.equal(undefined);
    });

    it('never registers cancellation for a non-cancelable token', async () => {
        const { channel, factory } = await setup();

        const settled = settle(
            channel.call(
                new RpcMessage.Request(0, 'ISvc', 'Work', []),
                Timeout.infiniteTimeSpan,
                CancellationToken.none,
            ),
        );

        await tick();
        const requestId = RpcMessage.Request.fromNetwork(
            firstOfType(factory.last!.writes, Network.Message.Type.Request)!,
        ).Id;
        expect(firstOfType(factory.last!.writes, Network.Message.Type.Cancel)).to.equal(undefined);

        // Settle it so nothing is left pending.
        factory.last!.observer.next(responseFrame(requestId));
        expect((await settled).ok).to.equal(true);
    });

    it('suppresses the request entirely when the token is already cancelled at call time', async () => {
        const { channel, factory } = await setup();
        const cts = new CancellationTokenSource();
        cts.cancel();

        const settled = settle(
            channel.call(new RpcMessage.Request(0, 'ISvc', 'Work', []), Timeout.infiniteTimeSpan, cts.token),
        );

        await tick();

        // Mirrors .NET: a call abandoned before it is sent never reaches the wire —
        // no request, and hence no (pointless) cancel for a request the peer never saw.
        expect(factory.last!.writes, `writes were ${typesOf(factory.last!.writes)}`).to.have.lengthOf(0);

        const outcome = await settled;
        expect(outcome.ok).to.equal(false);
        expect((outcome as any).err).to.be.instanceOf(OperationCanceledError);
    });

    it('settles an in-flight call when the channel is disposed, and disposes its cancellation registration', async () => {
        const { channel, factory } = await setup();
        const cts = new CancellationTokenSource();

        const settled = settle(
            channel.call(new RpcMessage.Request(0, 'ISvc', 'Work', []), Timeout.infiniteTimeSpan, cts.token),
        );
        await tick();

        await channel.disposeAsync();

        // The call is faulted (not left hanging under the infinite timeout).
        const outcome = await settled;
        expect(outcome.ok).to.equal(false);
        expect((outcome as any).err).to.be.instanceOf(ObjectDisposedError);

        // Settling ran call()'s finally, disposing the registration — so firing the
        // token afterwards emits nothing (no retained channel-capturing closure).
        const writesAfterDispose = factory.last!.writes.length;
        cts.cancel();
        await tick();
        expect(factory.last!.writes.length).to.equal(writesAfterDispose);
    });

    it('keeps Request before Cancel even when the token fires while the first call is still connecting', async () => {
        const address = new DeferredMockAddress();
        const factory = new StubMessageStreamFactory();
        const channel = RpcChannel.create(
            address,
            {} as any as ConnectHelper,
            TimeSpan.fromSeconds(30),
            CancellationToken.none,
            { next() {}, error() {}, complete() {} } as Observer<RpcCallContext.Incomming>,
            factory,
        );

        // Do NOT await the message stream: the connect is still pending.
        const cts = new CancellationTokenSource();
        const settled = settle(
            channel.call(new RpcMessage.Request(0, 'ISvc', 'Work', []), Timeout.infiniteTimeSpan, cts.token),
        );

        await tick();
        // Nothing on the wire yet — the stream does not exist until connect resolves.
        expect(factory.last).to.equal(undefined);

        // Fire the token mid-connect: the cancel's write is queued behind the request's.
        cts.cancel();
        await tick();
        expect(factory.last).to.equal(undefined);

        // Now let the connection complete; both queued writes flush in order.
        address.resolveConnect();
        await tick();

        expect(typesOf(factory.last!.writes)).to.deep.equal(['Request', 'Cancel']);

        const outcome = await settled;
        expect(outcome.ok).to.equal(false);
        expect((outcome as any).err).to.be.instanceOf(OperationCanceledError);
    });
});
