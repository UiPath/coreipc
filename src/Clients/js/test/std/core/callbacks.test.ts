import { Observer } from 'rxjs';

import {
    CancellationToken,
    TimeSpan,
    ChannelManager,
    ConfigStore,
    ContractStore,
    CallbackStoreImpl,
    IRpcChannel,
    IRpcChannelFactory,
    IMessageStream,
    RpcMessage,
    RpcCallContext,
    Address,
    ConnectHelper,
    Socket,
    Trace,
} from '../../../src/std';

import { NodeAddressBuilder } from '../../../src/node/NodeAddressBuilder';
import { MockServiceProvider } from '../../infrastructure';

import { expect } from 'chai';

class MockAddress extends Address {
    get key() { return 'mock:pipe'; }
    async connect(helper: ConnectHelper, timeout: TimeSpan, ct: CancellationToken): Promise<Socket> {
        throw new Error('Should not be called in unit tests');
    }
}

class MockRpcChannel implements IRpcChannel {
    get isDisposed() { return false; }
    async disposeAsync(): Promise<void> {}
    async call(request: RpcMessage.Request, timeout: TimeSpan, ct: CancellationToken): Promise<RpcMessage.Response> {
        return new RpcMessage.Response(request.Id, JSON.stringify(null), null);
    }
}

class ObserverCapturingFactory implements IRpcChannelFactory {
    observer: Observer<RpcCallContext.Incomming> | undefined;

    create(
        address: Address,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        observer: Observer<RpcCallContext.Incomming>,
        messageStreamFactory?: IMessageStream.Factory,
    ): IRpcChannel {
        this.observer = observer;
        return new MockRpcChannel();
    }
}

class MockService {
    DoWork(): Promise<void> { throw void 0; }
}

const endpoint = 'IArithmeticCallback';

async function arrange(callback?: object) {
    const callbackStore = new CallbackStoreImpl();
    const address = new MockAddress();
    if (callback) {
        callbackStore.set(endpoint, address, callback);
    }

    const sp = new MockServiceProvider<NodeAddressBuilder>({
        implementation: {
            configStore: new ConfigStore<NodeAddressBuilder>(
                new MockServiceProvider<NodeAddressBuilder>({
                    implementation: { contractStore: new ContractStore() },
                }),
            ),
            contractStore: new ContractStore(),
            callbackStore,
        },
    });

    const factory = new ObserverCapturingFactory();
    await new ChannelManager(sp, address, factory).invokeMethod(MockService, 'DoWork', []);

    const newRequest = (methodName: string, parameters: string[]) => {
        const request = new RpcMessage.Request(0, endpoint, methodName, parameters);
        request.Id = 'server-request-1';
        return request;
    };

    const dispatch = (methodName: string, parameters: string[] = []) =>
        new Promise<RpcMessage.Response>(resolve => {
            factory.observer!.next(new RpcCallContext.Incomming(newRequest(methodName, parameters), async response => resolve(response)));
        });

    // What the observer's promise does when the response can't be sent, for instance after the connection closed.
    const dispatchWithFailingRespond = (methodName: string, parameters: string[] = []) =>
        factory.observer!.next(
            new RpcCallContext.Incomming(newRequest(methodName, parameters), async () => {
                throw new Error('The connection closed.');
            }),
        ) as unknown as Promise<void>;

    return Object.assign(dispatch, { withFailingRespond: dispatchWithFailingRespond });
}

function revokedProxy(): object {
    const { proxy, revoke } = Proxy.revocable({}, {});
    revoke();
    return proxy;
}

describe('incoming callback requests', () => {
    it('should respond with an error when no callback is defined for the endpoint', async () => {
        const dispatch = await arrange();

        const response = await dispatch('Add', ['1', '2']);

        expect(response.RequestId).to.equal('server-request-1');
        expect(response.Data).to.equal(null);
        expect(response.Error?.Message).to.equal(`A callback is not defined for endpoint "${endpoint}".`);
    });

    it('should respond with an error when the callback lacks the method', async () => {
        const dispatch = await arrange({});

        const response = await dispatch('Add', ['1', '2']);

        expect(response.Data).to.equal(null);
        expect(response.Error?.Message).to.contain('does not expose a method named "Add"');
    });

    it('should respond with an error when the parameters are not valid JSON', async () => {
        const dispatch = await arrange({ Add: async (x: number, y: number) => x + y });

        const response = await dispatch('Add', ['{']);

        expect(response.Data).to.equal(null);
        expect(response.Error).to.not.equal(null);
    });

    it('should respond with the error thrown by the callback', async () => {
        const dispatch = await arrange({ Add: async () => { throw new Error('boom'); } });

        const response = await dispatch('Add', ['1', '2']);

        expect(response.Data).to.equal(null);
        expect(response.Error?.Message).to.equal('boom');
    });

    for (const [name, thrown, message] of [
        ['undefined', undefined, 'undefined'],
        ['null', null, 'null'],
        ['a number', 42, '42'],
        ['a string', 'boom', 'boom'],
        ['an object without a prototype', Object.create(null), '[object Object]'],
        ['a revoked proxy', revokedProxy(), 'object'],
        ['an Error whose message is a number', Object.assign(new Error('x'), { message: 42 }), '42'],
    ] as Array<[string, unknown, string]>) {
        it(`should respond with an error when the callback throws ${name}`, async () => {
            const dispatch = await arrange({ Add: async () => { throw thrown; } });

            const response = await dispatch('Add', ['1', '2']);

            expect(response.Data).to.equal(null);
            expect(response.Error?.Message).to.equal(message);
        });
    }

    it('should log, rather than leave unhandled, a response that cannot be sent', async () => {
        const dispatch = await arrange({ Add: async (x: number, y: number) => x + y });
        const logged = new Array<string>();
        const listener = Trace.addListener(entry => logged.push(String(entry)));
        try {
            await dispatch.withFailingRespond('Add', ['1', '2']);
        } finally {
            listener.dispose();
        }

        expect(logged.some(entry => entry.includes('The connection closed.'))).to.equal(true);
    });

    it('should keep serving callbacks after a failed one', async () => {
        const dispatch = await arrange({ Add: async (x: number, y: number) => x + y });

        await dispatch('Subtract');
        const response = await dispatch('Add', ['1', '2']);

        expect(response.Error).to.equal(null);
        expect(response.Data).to.equal('3');
    });
});
