import 'reflect-metadata';

import {
    Address,
    CallbackStoreImpl,
    CancellationToken,
    CancellationTokenSource,
    ChannelManager,
    ConnectHelper,
    ContractStore,
    RpcCallContext,
    RpcMessage,
    Socket,
    TimeSpan,
} from '../../../src/std';

import { NodeAddressBuilder } from '../../../src/node/NodeAddressBuilder';
import { MockServiceProvider } from '../../infrastructure';

import { expect } from 'chai';

// A callback contract shaped as the @ipc decorators would leave it: registered
// in the contract store, with parameter-type metadata stamped on the prototype.
class IProbeCallback {
    Ping(_message: string, _ct: CancellationToken): Promise<string> {
        throw void 0;
    }
    Wave(_message: string, _signal: AbortSignal): Promise<string> {
        throw void 0;
    }
    Plain(_message: string): Promise<string> {
        throw void 0;
    }
}

function stampContract(store: ContractStore): void {
    const descriptor = store.getOrCreate(IProbeCallback);
    descriptor.operations.getOrCreate('Ping' as never);
    descriptor.operations.getOrCreate('Wave' as never);
    descriptor.operations.getOrCreate('Plain' as never);

    Reflect.defineMetadata(
        'design:paramtypes',
        [String, CancellationToken],
        IProbeCallback.prototype,
        'Ping',
    );
    Reflect.defineMetadata(
        'design:paramtypes',
        [String, AbortSignal],
        IProbeCallback.prototype,
        'Wave',
    );
    Reflect.defineMetadata('design:paramtypes', [String], IProbeCallback.prototype, 'Plain');
}

class MockAddress extends Address {
    get key() {
        return 'mock:callback-cancellation';
    }
    async connect(_h: ConnectHelper, _t: TimeSpan, _ct: CancellationToken): Promise<Socket> {
        throw new Error('unused');
    }
}

interface InvokeResult {
    received: any[];
    response: RpcMessage.Response | undefined;
    cts: CancellationTokenSource;
}

async function invokeCallback(options: {
    method: string;
    params: string[];
    withContract?: boolean;
}): Promise<InvokeResult> {
    const address = new MockAddress();

    const contractStore = new ContractStore();
    if (options.withContract !== false) {
        stampContract(contractStore);
    }

    const received: any[] = [];
    const handler = {
        async Ping(message: string, ct: CancellationToken): Promise<string> {
            received.push({ message, ct });
            return 'pong';
        },
        async Wave(message: string, signal: AbortSignal): Promise<string> {
            received.push({ message, signal });
            return 'wave';
        },
        async Plain(message: string): Promise<string> {
            received.push({ message });
            return 'plain';
        },
    };

    const callbackStore = new CallbackStoreImpl();
    callbackStore.set('IProbeCallback', address, handler);

    const sp = new MockServiceProvider<NodeAddressBuilder>({
        implementation: { contractStore, callbackStore },
    });

    const cm = new ChannelManager(sp, address, undefined as any);

    const cts = new CancellationTokenSource();
    const request = new RpcMessage.Request(0, 'IProbeCallback', options.method, options.params);
    request.Id = '1';

    let response: RpcMessage.Response | undefined;
    const context = new RpcCallContext.Incomming(
        request,
        async (r) => {
            response = r;
        },
        cts.token,
    );

    await (cm as any)._incommingCallObserver.next(context);

    return { received, response, cts };
}

const HELLO = JSON.stringify('hi');
const BLANK = JSON.stringify(''); // how the peer serializes a cancellation slot

describe('callback (callee) cancellation injection', () => {
    it('injects the per-call CancellationToken into a trailing token parameter', async () => {
        const { received, response, cts } = await invokeCallback({
            method: 'Ping',
            params: [HELLO, BLANK],
        });

        expect(received).to.have.lengthOf(1);
        expect(received[0].message).to.equal('hi');
        expect(received[0].ct).to.equal(cts.token);
        expect(response?.Data).to.equal(JSON.stringify('pong'));
    });

    it('the injected token actually fires when the call is cancelled', async () => {
        const { received, cts } = await invokeCallback({ method: 'Ping', params: [HELLO, BLANK] });

        const ct: CancellationToken = received[0].ct;
        expect(ct.isCancellationRequested).to.equal(false);

        cts.cancel();

        expect(ct.isCancellationRequested).to.equal(true);
    });

    it('injects a bridged AbortSignal into a trailing AbortSignal parameter', async () => {
        const { received, cts } = await invokeCallback({ method: 'Wave', params: [HELLO, BLANK] });

        const signal: AbortSignal = received[0].signal;
        expect(signal).to.be.instanceOf(AbortSignal);
        expect(signal.aborted).to.equal(false);

        cts.cancel();

        expect(signal.aborted).to.equal(true);
    });

    it('does not touch arguments when the contract has no trailing cancellation param', async () => {
        const { received } = await invokeCallback({ method: 'Plain', params: [HELLO] });

        expect(received[0].message).to.equal('hi');
        expect(Object.keys(received[0])).to.deep.equal(['message']);
    });

    it('leaves arguments untouched when no contract metadata is registered', async () => {
        // The common bare-endpoint / interface callback: the blanked slot must
        // be passed through verbatim rather than guessed at.
        const { received } = await invokeCallback({
            method: 'Ping',
            params: [HELLO, BLANK],
            withContract: false,
        });

        expect(received[0].message).to.equal('hi');
        expect(received[0].ct).to.equal('');
    });
});

describe('ContractStore.maybeGetByEndpoint', () => {
    it('finds a registered contract by its endpoint name', () => {
        const store = new ContractStore();
        stampContract(store);

        const descriptor = store.maybeGetByEndpoint('IProbeCallback');

        expect(descriptor).to.not.equal(undefined);
        expect(descriptor!.endpoint).to.equal('IProbeCallback');
    });

    it('returns undefined for an unknown endpoint', () => {
        const store = new ContractStore();
        stampContract(store);

        expect(store.maybeGetByEndpoint('Nope')).to.equal(undefined);
    });
});

describe('OperationDescriptor cancellation-kind flags', () => {
    it('detects a trailing CancellationToken vs AbortSignal vs neither', () => {
        const store = new ContractStore();
        stampContract(store);
        const operations = store.maybeGetByEndpoint('IProbeCallback')!.operations;

        const ping = operations.maybeGet('Ping' as never)!;
        expect(ping.hasEndingCancellationToken).to.equal(true);
        expect(ping.hasEndingAbortSignal).to.equal(false);

        const wave = operations.maybeGet('Wave' as never)!;
        expect(wave.hasEndingAbortSignal).to.equal(true);
        expect(wave.hasEndingCancellationToken).to.equal(false);

        const plain = operations.maybeGet('Plain' as never)!;
        expect(plain.hasEndingCancellationToken).to.equal(false);
        expect(plain.hasEndingAbortSignal).to.equal(false);
    });
});
