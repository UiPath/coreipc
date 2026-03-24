import { Observer } from 'rxjs';

import {
    CancellationToken,
    TimeSpan,
    Wire,
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

// --- Test doubles ---

class MockAddress extends Address {
    constructor(public readonly name: string) { super(); }
    get key() { return `mock:${this.name}`; }
    async connect(helper: ConnectHelper, timeout: TimeSpan, ct: CancellationToken): Promise<Socket> {
        throw new Error('Should not be called in unit tests');
    }
}

class MockRpcChannel implements IRpcChannel {
    disposed = false;
    callCount = 0;

    get isDisposed() { return this.disposed; }

    async disposeAsync(): Promise<void> {
        this.disposed = true;
    }

    async call(request: RpcMessage.Request, timeout: TimeSpan, ct: CancellationToken): Promise<RpcMessage.Response> {
        this.callCount++;
        return new RpcMessage.Response(request.Id, JSON.stringify(null), null);
    }
}

class MockRpcChannelFactory implements IRpcChannelFactory {
    readonly channels: MockRpcChannel[] = [];

    create(
        address: Address,
        connectHelper: ConnectHelper,
        connectTimeout: TimeSpan,
        ct: CancellationToken,
        observer: Observer<RpcCallContext.Incomming>,
        messageStreamFactory?: IMessageStream.Factory,
    ): IRpcChannel {
        const channel = new MockRpcChannel();
        this.channels.push(channel);
        return channel;
    }
}

class MockService {
    DoWork(): Promise<void> { throw void 0; }
}

function createMockServiceProvider() {
    return new MockServiceProvider<NodeAddressBuilder>({
        implementation: {
            configStore: new ConfigStore<NodeAddressBuilder>(
                new MockServiceProvider<NodeAddressBuilder>({
                    implementation: {
                        contractStore: new ContractStore(),
                    },
                }),
            ),
            contractStore: new ContractStore(),
            callbackStore: new CallbackStoreImpl(),
        },
    });
}

// --- Tests ---

describe('dispose', () => {

    describe(`${ChannelManager.name}`, () => {
        it('should dispose the underlying channel', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const address = new MockAddress('pipe-1');
            const cm = new ChannelManager(sp, address, factory);

            // Trigger channel creation by invoking a method
            await cm.invokeMethod(MockService, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(1);

            const channel = factory.channels[0];
            expect(channel.disposed).to.equal(false);

            await cm.disposeAsync();

            expect(channel.disposed).to.equal(true);
        });

        it('should be a no-op when no channel was ever created', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const address = new MockAddress('pipe-1');
            const cm = new ChannelManager(sp, address, factory);

            // Dispose without ever calling invokeMethod
            await cm.disposeAsync();

            expect(factory.channels).to.have.lengthOf(0);
        });

        it('should be a no-op when the channel is already disposed', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const address = new MockAddress('pipe-1');
            const cm = new ChannelManager(sp, address, factory);

            await cm.invokeMethod(MockService, 'DoWork', []);
            const channel = factory.channels[0];

            // Pre-dispose the channel
            await channel.disposeAsync();
            expect(channel.disposed).to.equal(true);

            // ChannelManager.disposeAsync should not throw
            await cm.disposeAsync();
        });

        it('should create a new channel on next call after disposal', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const address = new MockAddress('pipe-1');
            const cm = new ChannelManager(sp, address, factory);

            // First call creates channel #1
            await cm.invokeMethod(MockService, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(1);

            const firstChannel = factory.channels[0];
            expect(firstChannel.callCount).to.equal(1);

            // Dispose the channel manager
            await cm.disposeAsync();
            expect(firstChannel.disposed).to.equal(true);

            // Second call should create channel #2
            await cm.invokeMethod(MockService, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(2);

            const secondChannel = factory.channels[1];
            expect(secondChannel.callCount).to.equal(1);
            expect(secondChannel.disposed).to.equal(false);
        });
    });

    describe(`${Wire.name}`, () => {
        it('disposeChannel should dispose and remove the channel for a specific address', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const wire = new Wire(sp, factory);

            const address = new MockAddress('pipe-1');
            const proxyId = { service: MockService, address };

            // Invoke to create a channel
            await wire.invokeMethod(proxyId, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(1);
            expect(factory.channels[0].disposed).to.equal(false);

            // Dispose the channel
            await wire.disposeChannel(address.key);
            expect(factory.channels[0].disposed).to.equal(true);
        });

        it('disposeChannel should be a no-op for an unknown address', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const wire = new Wire(sp, factory);

            // Should not throw
            await wire.disposeChannel('mock:unknown');
        });

        it('disposeAllChannels should dispose every open channel', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const wire = new Wire(sp, factory);

            const addr1 = new MockAddress('pipe-1');
            const addr2 = new MockAddress('pipe-2');

            await wire.invokeMethod({ service: MockService, address: addr1 }, 'DoWork', []);
            await wire.invokeMethod({ service: MockService, address: addr2 }, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(2);

            await wire.disposeAllChannels();

            expect(factory.channels[0].disposed).to.equal(true);
            expect(factory.channels[1].disposed).to.equal(true);
        });

        it('disposeAllChannels should be a no-op when no channels exist', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const wire = new Wire(sp, factory);

            // Should not throw
            await wire.disposeAllChannels();
        });

        it('should create a new channel after disposeChannel', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const wire = new Wire(sp, factory);

            const address = new MockAddress('pipe-1');
            const proxyId = { service: MockService, address };

            // First call
            await wire.invokeMethod(proxyId, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(1);

            // Dispose
            await wire.disposeChannel(address.key);
            expect(factory.channels[0].disposed).to.equal(true);

            // Second call — should reconnect
            await wire.invokeMethod(proxyId, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(2);
            expect(factory.channels[1].disposed).to.equal(false);
            expect(factory.channels[1].callCount).to.equal(1);
        });

        it('should create new channels for all addresses after disposeAllChannels', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const wire = new Wire(sp, factory);

            const addr1 = new MockAddress('pipe-1');
            const addr2 = new MockAddress('pipe-2');
            const pid1 = { service: MockService, address: addr1 };
            const pid2 = { service: MockService, address: addr2 };

            // Create channels for both addresses
            await wire.invokeMethod(pid1, 'DoWork', []);
            await wire.invokeMethod(pid2, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(2);

            // Dispose all
            await wire.disposeAllChannels();
            expect(factory.channels[0].disposed).to.equal(true);
            expect(factory.channels[1].disposed).to.equal(true);

            // Reconnect both
            await wire.invokeMethod(pid1, 'DoWork', []);
            await wire.invokeMethod(pid2, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(4);
            expect(factory.channels[2].disposed).to.equal(false);
            expect(factory.channels[3].disposed).to.equal(false);
        });

        it('should allow multiple dispose-reconnect cycles', async () => {
            const factory = new MockRpcChannelFactory();
            const sp = createMockServiceProvider();
            const wire = new Wire(sp, factory);

            const address = new MockAddress('pipe-1');
            const proxyId = { service: MockService, address };

            for (let cycle = 0; cycle < 3; cycle++) {
                await wire.invokeMethod(proxyId, 'DoWork', []);
                await wire.disposeAllChannels();
            }

            expect(factory.channels).to.have.lengthOf(3);
            for (const ch of factory.channels) {
                expect(ch.disposed).to.equal(true);
                expect(ch.callCount).to.equal(1);
            }

            // One final call that stays open
            await wire.invokeMethod(proxyId, 'DoWork', []);
            expect(factory.channels).to.have.lengthOf(4);
            expect(factory.channels[3].disposed).to.equal(false);
        });
    });
});
