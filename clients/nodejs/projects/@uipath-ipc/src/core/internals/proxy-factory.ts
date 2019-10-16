// tslint:disable: ban-types
// tslint:disable: only-arrow-functions

import * as BrokerMessage from './broker-message';
import { Broker, IBroker } from './broker';
import { ArgumentNullError } from '../../foundation/errors/argument-null-error';
import { rtti } from '../surface/rtti';
import { CancellationToken } from '../../foundation/threading/cancellation-token';
import { RemoteError } from '../surface/remote-error';
import { PublicConstructor, ITraceCategory } from '@foundation/utils';
import { Trace, OperationCanceledError } from '../..';
import { AggregateError } from '@foundation/errors';

const symbolofMaybeProxyCtor = Symbol('maybe:ProxyFactory');

/* @internal */
export const symbolofBroker = Symbol('broker');

interface IProxyCtor<TService> {
    new(broker: IBroker): TService;
    prototype: IProxyPrototype<TService>;
}
interface IProxyPrototype<TService> {
    [methodName: string]: IMethod;
}
interface IProxy<TService> {
    [symbolofBroker]: IBroker;
}
type IMethod = (...args: any[]) => any;

/* @internal */
export class Generator<TService> {
    public static generate<TService>(sampleCtor: PublicConstructor<TService>): IProxyCtor<TService> {
        if (!sampleCtor) { throw new ArgumentNullError('sampleCtor'); }

        const instance = new Generator<TService>(sampleCtor);
        instance.run();
        return instance._generatedCtor;
    }

    private readonly _generatedCtor: IProxyCtor<TService>;
    private readonly _traceCategory: ITraceCategory;

    constructor(private readonly _sampleCtor: PublicConstructor<TService>) {
        this._generatedCtor = Generator.createCtor<TService>();
        this._traceCategory = Trace.category(`generated-proxy(${_sampleCtor})`);
    }

    private run(): void {
        const methodNames = this.enumerateSampleMethodNames();
        for (const methodName of methodNames) {
            const generatedMethod = this.generateMethod(methodName);
            this._generatedCtor.prototype[methodName] = generatedMethod;
        }
    }

    private generateMethod(methodName: string): IMethod {
        const maybeMethodInfo = rtti.ClassInfo.get(this._sampleCtor as any).tryGetMethod(methodName);
        const hasCancellationToken = maybeMethodInfo ? maybeMethodInfo.hasCancellationToken : false;

        const normalizeArgs = Normalizers.getArgListNormalizer(hasCancellationToken);
        const maybeReturnCtor = maybeMethodInfo ? maybeMethodInfo.maybeReturnValueCtor : null;
        const normalizeResult = Normalizers.getResultNormalizer(maybeReturnCtor);

        const traceCategory = this._traceCategory;

        return async function (this: IProxy<TService>) {
            const args = normalizeArgs([...arguments]);

            const brokerOutboundRequest = new BrokerMessage.OutboundRequest(methodName, args);

            Trace.log(`GeneratedProxy: Sending brokerRequest === ${JSON.stringify(brokerOutboundRequest)}`);
            try {
                const brokerResponse = await this[symbolofBroker].sendReceiveAsync(brokerOutboundRequest);
                Trace.log(`GeneratedProxy: brokerResponse === ${JSON.stringify(brokerOutboundRequest)}`);

                if (brokerResponse.maybeError) {
                    throw new RemoteError(brokerResponse.maybeError, methodName);
                } else {
                    return normalizeResult(brokerResponse.maybeResult);
                }
            } catch (error) {
                traceCategory.log(`caught error ${JSON.stringify(error)}`);

                if (error instanceof OperationCanceledError || error instanceof RemoteError) {
                    throw error;
                } else {
                    throw new AggregateError(`An error was caught while trying to issue a remote procedure call.\r\nMethod name: ${methodName}`, error);
                }
            }
        };
    }

    public enumerateSampleMethodNames(): string[] {
        return Reflect.ownKeys(this._sampleCtor.prototype).filter(this.refersToAMethod.bind(this));
    }
    public refersToAMethod(key: string | number | symbol): key is string {
        try {
            return typeof key === 'string' && typeof this._sampleCtor.prototype[key] === 'function' && this._sampleCtor !== this._sampleCtor.prototype[key];
        } catch (error) {
            return false;
        }
    }

    private static createCtor<TService>(): IProxyCtor<TService> {
        const result = function (this: IProxy<TService>, broker: Broker): void {
            this[symbolofBroker] = broker;
        } as any as IProxyCtor<TService>;
        result.prototype = {};
        return result;
    }
}

class Normalizers {
    public static getArgListNormalizer(hasCancellationToken: boolean): (args: any[]) => any[] {
        return hasCancellationToken ? Normalizers.normalizeArgList__hasCancellationToken__ : Normalizers.normalizeArgList__doesNotHaveCancellationToken__;
    }

    private static normalizeArgList__hasCancellationToken__(args: any[]): any[] {
        if (args.length > 0 && args[args.length - 1] === undefined) {
            args.splice(args.length - 1);
        }

        if (args.length === 0 || (!(args[args.length - 1] instanceof CancellationToken))) {
            return [...args, CancellationToken.none];
        } else {
            return args;
        }
    }
    private static normalizeArgList__doesNotHaveCancellationToken__(args: any[]): any[] {
        if (args.length > 0 && args[args.length - 1] === undefined) {
            args.splice(args.length - 1);
        }

        return args;
    }

    public static getResultNormalizer<T>(maybeCtor: Function | null): (result: T) => T {
        if (maybeCtor) {
            return function (result: T) {
                if (result) {
                    (result as any).__proto__ = maybeCtor.prototype;
                }
                return result;
            };
        } else {
            return this.normalizeResult__default__;
        }
    }
    private static normalizeResult__default__<T>(result: T): T {
        return result;
    }
}

/* @internal */
export class ProxyFactory {
    public static create<TService>(
        sampleCtor: PublicConstructor<TService>,
        broker: IBroker
    ): TService {
        if (!sampleCtor) { throw new ArgumentNullError('sampleCtor'); }
        if (!broker) { throw new ArgumentNullError('broker'); }

        const sampleProto = sampleCtor.prototype;

        const ownKeys = Reflect.ownKeys(sampleProto);
        let mustCreate = true;
        for (const key of ownKeys) {
            if (key === symbolofMaybeProxyCtor) {
                mustCreate = false;
                break;
            }
        }
        const ctor: IProxyCtor<TService> = mustCreate
            ? sampleProto[symbolofMaybeProxyCtor] = Generator.generate(sampleCtor)
            : sampleProto[symbolofMaybeProxyCtor];

        const result = new ctor(broker);
        return result;
    }
}
