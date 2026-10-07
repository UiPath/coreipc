import { Observer, Subject } from 'rxjs';

import {
    Address,
    CancellationToken,
    ConnectHelper,
    IMessageStream,
    Network,
    RpcCallContext,
    RpcChannel,
    RpcMessage,
    Socket,
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
        return 'mock:rpc-cancellation';
    }
    async connect(_h: ConnectHelper, _t: TimeSpan, _ct: CancellationToken): Promise<Socket> {
        return new MockSocket();
    }
}

// A message stream that lets the test push inbound Network.Messages straight
// into the channel's network observer, bypassing real socket framing.
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

function requestFrame(id: string, endpoint: string, method: string, params: string[]): Network.Message {
    const request = new RpcMessage.Request(0, endpoint, method, params);
    request.Id = id;
    return request.toNetwork();
}

function cancelFrame(id: string): Network.Message {
    return new RpcMessage.CancellationRequest(id).toNetwork();
}

async function setup() {
    const incomming: RpcCallContext.Incomming[] = [];
    const rpcObserver: Observer<RpcCallContext.Incomming> = {
        next: (context) => {
            incomming.push(context);
        },
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

    // Wait for the (async) message-stream creation, which captures the observer.
    await (channel as any)._$messageStream;

    return { channel, factory: factory!, incomming };
}

// --- Tests ---

describe(`${RpcChannel.name} (callee cancellation)`, () => {
    it('exposes a live token per incoming call and cancels it on a Cancel frame', async () => {
        const { factory, incomming } = await setup();

        factory.last!.observer.next(requestFrame('1', 'IProbe', 'Work', []));

        expect(incomming).to.have.lengthOf(1);
        const call = incomming[0];
        expect(call.ct.isCancellationRequested).to.equal(false);

        factory.last!.observer.next(cancelFrame('1'));

        expect(call.ct.isCancellationRequested).to.equal(true);
    });

    it('cancels only the call whose id matches', async () => {
        const { factory, incomming } = await setup();

        factory.last!.observer.next(requestFrame('1', 'IProbe', 'Work', []));
        factory.last!.observer.next(requestFrame('2', 'IProbe', 'Work', []));

        factory.last!.observer.next(cancelFrame('2'));

        expect(incomming[0].ct.isCancellationRequested).to.equal(false);
        expect(incomming[1].ct.isCancellationRequested).to.equal(true);
    });

    it('ignores a Cancel frame that arrives after the call completed', async () => {
        const { factory, incomming } = await setup();

        factory.last!.observer.next(requestFrame('3', 'IProbe', 'Work', []));
        const call = incomming[0];

        await call.respond(new RpcMessage.Response('3', JSON.stringify(null), null));

        expect(() => factory.last!.observer.next(cancelFrame('3'))).to.not.throw();
        expect(call.ct.isCancellationRequested).to.equal(false);
    });

    it('does not throw on a Cancel frame for an unknown request id', async () => {
        const { factory } = await setup();

        expect(() => factory.last!.observer.next(cancelFrame('unknown'))).to.not.throw();
    });

    it('cancels in-flight calls when the channel is disposed', async () => {
        const { channel, factory, incomming } = await setup();

        factory.last!.observer.next(requestFrame('4', 'IProbe', 'Work', []));
        const call = incomming[0];

        await channel.disposeAsync();

        expect(call.ct.isCancellationRequested).to.equal(true);
    });
});
