import '../../jest-extensions';
import * as BrokerMessage from '../../../src/core/internals/broker/broker-message'
import { Generator, ProxyFactory } from '../../../src/core/internals/proxy-factory';
import { IBroker } from '../../../src/core/internals/broker/broker';
import { PromisePal, IpcClient, __returns__ } from '../../../src';
import { InternalIpcClientConfig } from '../../../src/core/surface/ipc-client';

describe('Core-Internals-ProxyFactory', () => {
    class Integer {
        constructor(public readonly value: number) { }
    }
    class IMockService {
        @__returns__(Integer)
        // @ts-ignore
        public addNumbersAsync(x: Integer, y: Integer): Promise<Integer> { throw null; }
    }

    class MockBroker implements IBroker {
        // tslint:disable-next-line: max-line-length
        public sendReceiveAsync: (brokerRequest: BrokerMessage.Request) => Promise<BrokerMessage.Response> = brokerRequest => new Promise<BrokerMessage.Response>((resolve, reject) => { });

        public disposeAsync(): Promise<void> { return PromisePal.completedPromise; }
    }

    test(`Generator.refersToAMethod works`, () => {
        const generator = new Generator<IMockService>(IMockService as any);
        expect(generator.refersToAMethod(undefined as any)).toBe(false);
        expect(generator.refersToAMethod(null as any)).toBe(false);
        expect(generator.refersToAMethod(true as any)).toBe(false);
        expect(generator.refersToAMethod('')).toBe(false);
        expect(generator.refersToAMethod('test')).toBe(false);
        expect(generator.refersToAMethod(0)).toBe(false);
        expect(generator.refersToAMethod(100)).toBe(false);
        expect(generator.refersToAMethod('constructor')).toBe(false);
        expect(generator.refersToAMethod('somethingElse')).toBe(false);
        expect(generator.refersToAMethod('addNumbersAsync')).toBe(true);
    });

    test(`Generator.enumerateSampleMethodNames works`, () => {
        const generator = new Generator<IMockService>(IMockService as any);
        const methodNames = generator.enumerateSampleMethodNames();
        expect(methodNames).toEqual(['addNumbersAsync']);
    });

    test(`Generator.generate works`, async () => {
        const mockBroker = new MockBroker();
        mockBroker.sendReceiveAsync = jest.fn(() => PromisePal.fromResult(new BrokerMessage.Response(new Integer(30), null)));

        const proxyCtor = Generator.generate(IMockService);
        const proxy = new proxyCtor(mockBroker);

        const promise = proxy.addNumbersAsync(new Integer(10), new Integer(20));

        expect(mockBroker.sendReceiveAsync).toHaveBeenCalledWith(new BrokerMessage.OutboundRequest('addNumbersAsync', [new Integer(10), new Integer(20)]));
        await expect(promise).resolves.toEqual(new Integer(30));
    });

    test(`ProxyFactory.create works`, async () => {
        const mockBroker = new MockBroker();
        mockBroker.sendReceiveAsync = jest.fn(() => PromisePal.fromResult(new BrokerMessage.Response(new Integer(30), null)));

        const proxy = ProxyFactory.create(IMockService, mockBroker);

        expect(proxy.addNumbersAsync).toBeInstanceOf(Function);
        const promise = proxy.addNumbersAsync(new Integer(10), new Integer(20));

        expect(mockBroker.sendReceiveAsync).toHaveBeenCalledWith(new BrokerMessage.OutboundRequest('addNumbersAsync', [new Integer(10), new Integer(20)]));
        await expect(promise).resolves.toEqual(new Integer(30));
    });

    class IMockService2 {
        public addNumbersAsync(x: number, y: number): Promise<number> { throw null; }
    }

    test(`ProxyFactory.create works 2`, async () => {
        const mockBroker = new MockBroker();
        mockBroker.sendReceiveAsync = jest.fn(() => PromisePal.fromResult(new BrokerMessage.Response(new Integer(30), null)));

        const proxy1 = ProxyFactory.create(Object, mockBroker);

        const proxy2 = ProxyFactory.create(IMockService2, mockBroker);
        expect(proxy2.addNumbersAsync).toBeInstanceOf(Function);
    });

    test(`IpcClient.ctor works`, async () => {
        const mockBroker = new MockBroker();
        mockBroker.sendReceiveAsync = jest.fn(() => PromisePal.fromResult(new BrokerMessage.Response(new Integer(30), null)));

        const client = new IpcClient('foo', IMockService, config => {
            const asInternal = config as InternalIpcClientConfig<IMockService>;
            asInternal.maybeBroker = mockBroker;
        });
        const proxy = client.proxy;

        const promise = proxy.addNumbersAsync(new Integer(10), new Integer(20));

        expect(mockBroker.sendReceiveAsync).toHaveBeenCalledWith(new BrokerMessage.OutboundRequest('addNumbersAsync', [new Integer(10), new Integer(20)]));
        await expect(promise).resolves.toEqual(new Integer(30));
    });
});
