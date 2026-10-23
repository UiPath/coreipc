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

    return (methodName: string, parameters: string[] = []) => {
        const request = new RpcMessage.Request(0, endpoint, methodName, parameters);
        request.Id = 'server-request-1';

        return new Promise<RpcMessage.Response>(resolve => {
            factory.observer!.next(new RpcCallContext.Incomming(request, async response => resolve(response)));
        });
    };
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

    it('should keep serving callbacks after a failed one', async () => {
        const dispatch = await arrange({ Add: async (x: number, y: number) => x + y });

        await dispatch('Subtract');
        const response = await dispatch('Add', ['1', '2']);

        expect(response.Error).to.equal(null);
        expect(response.Data).to.equal('3');
    });
});
