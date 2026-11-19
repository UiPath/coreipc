import {
    Address,
    CancellationToken,
    CancellationTokenSource,
    ConfigStore,
    ConnectHelper,
    ContractStore,
    RpcRequestFactory,
    Socket,
    TimeSpan,
} from '../../../src/std';

import { NodeAddressBuilder } from '../../../src/node/NodeAddressBuilder';
import { MockServiceProvider } from '../../infrastructure';

import { expect } from 'chai';

class MockAddress extends Address {
    get key() {
        return 'mock:rpc-request-factory';
    }
    async connect(_h: ConnectHelper, _t: TimeSpan, _ct: CancellationToken): Promise<Socket> {
        throw new Error('unused');
    }
}

class Contract {
    Work(_ct: CancellationToken): Promise<void> {
        throw void 0;
    }
}

function serviceProvider() {
    const contractStore = new ContractStore();
    return new MockServiceProvider<NodeAddressBuilder>({
        implementation: {
            contractStore,
            configStore: new ConfigStore<NodeAddressBuilder>(
                new MockServiceProvider<NodeAddressBuilder>({
                    implementation: { contractStore: new ContractStore() },
                }),
            ),
        },
    });
}

describe('RpcRequestFactory (cancellation argument)', () => {
    it('serializes a live CancellationToken to an inert placeholder (no circular JSON)', () => {
        const sp = serviceProvider();
        const cts = new CancellationTokenSource();

        // A live token is a circular object graph (token -> source -> token).
        // Serializing it raw would throw "Converting circular structure to JSON".
        expect(() => JSON.stringify(cts.token)).to.throw();

        const [request, , ct] = RpcRequestFactory.create({
            sp,
            service: Contract,
            address: new MockAddress(),
            methodName: 'Work',
            args: [cts.token],
        });

        // The request must have been built without throwing, and every wire slot
        // must be plain, parseable JSON — the token was replaced by a placeholder.
        expect(() => request.Parameters.map((p) => JSON.parse(p))).to.not.throw();
        expect(JSON.parse(request.Parameters[0])).to.deep.equal({});

        // ...yet the live token is still returned, so the caller can bind local
        // cancellation and emit a CancellationRequest frame when it fires.
        expect(ct).to.equal(cts.token);
    });

    it('adapts and serializes an AbortSignal argument the same way', () => {
        const sp = serviceProvider();
        const controller = new AbortController();

        const [request, , ct] = RpcRequestFactory.create({
            sp,
            service: Contract,
            address: new MockAddress(),
            methodName: 'Work',
            args: [controller.signal],
        });

        expect(() => request.Parameters.map((p) => JSON.parse(p))).to.not.throw();
        expect(ct).to.be.instanceOf(CancellationToken);
        expect(ct.isCancellationRequested).to.equal(false);

        controller.abort();
        expect(ct.isCancellationRequested).to.equal(true);
    });

    it('uses CancellationToken.none (no wire arg detected) when none is passed', () => {
        const sp = serviceProvider();

        const [, , ct] = RpcRequestFactory.create({
            sp,
            service: Contract,
            address: new MockAddress(),
            methodName: 'Work',
            args: [],
        });

        expect(ct).to.equal(CancellationToken.none);
    });
});
