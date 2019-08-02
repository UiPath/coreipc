import * as net from 'net';
import { PromiseCompletionSource, CancellationToken } from '@uipath/ipc-helpers';
import { IBroker, Broker, IBrokerWithCallbacks, BrokerWithCallbacks } from '../internals/broker';
import { PipeWrapper } from '../internals/pipe-wrapper';
import { ChannelReader } from '../internals/channel-reader';
import { ChannelWriter } from '../internals/channel-writer';
import { Channel } from '../internals/channel';
import { Message } from './message';
import { IExecutor } from './executor';
import { InternalResponseMessage } from '../internals/internal-message';

type IMethod = (...args: any[]) => any;
interface IMethodContainer {
    [methodName: string]: IMethod;
}

/* @internal */
export class NamedPipeClientPal {
    public static connectAsync(pipeName: string): Promise<net.Socket> {
        const pcsConnected = new PromiseCompletionSource<net.Socket>();
        const fullName = `\\\\.\\pipe\\${pipeName}`;
        const socket = net.connect(fullName);
        socket.once('connect', () => pcsConnected.trySetResult(socket));
        socket.once('timeout', () => pcsConnected.trySetException(new Error(`Connecting to "${fullName}" timed out.`)));
        socket.once('error', (error) => pcsConnected.trySetException(error));
        return pcsConnected.promise;
    }

    public static createBroker(socket: net.Socket): IBroker {
        const pipe = new PipeWrapper(socket);
        return new Broker(
            new Channel(
                new ChannelReader(pipe),
                new ChannelWriter(pipe)));
    }

    public static createBrokerWithCallbacks(socket: net.Socket): IBrokerWithCallbacks {
        const pipe = new PipeWrapper(socket);
        return new BrokerWithCallbacks(
            new Channel(
                new ChannelReader(pipe),
                new ChannelWriter(pipe)));
    }

    public static generateProxy<TService>(servicePrototype: TService, executor: IExecutor): TService {
        const input = servicePrototype as any as IMethodContainer;
        const output: IMethodContainer = {} as any;

        for (const methodName in input) {
            if (typeof methodName === 'string' && input[methodName] instanceof Function) {
                output[methodName] = NamedPipeClientPal.generateMethodProxy(methodName, executor);
            }
        }

        return output as any;
    }
    private static generateMethodProxy(methodName: string, executor: IExecutor): IMethod {
        // tslint:disable-next-line: only-arrow-functions
        return function () {
            const paramsBuilder = {
                methodName,
                jsonArgs: new Array<string>(),
                maybeCancellationToken: null as CancellationToken | null,
                maybeMessage: null as Message<any> | null
            };

            for (const arg of arguments) {
                let obj = arg;

                if (obj instanceof CancellationToken) {
                    paramsBuilder.maybeCancellationToken = obj;
                    obj = {};
                }
                if (obj instanceof Message) {
                    paramsBuilder.maybeMessage = obj;
                    obj = {
                        Payload: obj.Payload
                    };
                }

                const json = JSON.stringify(obj);
                paramsBuilder.jsonArgs.push(json);
            }

            return executor.executeAsync(paramsBuilder);
        };
    }

    public static unwrap(response: InternalResponseMessage): any {
        if (response.Error) {
            throw new Error(response.Error.Message);
        } else {
            if (response.Data) {
                return JSON.parse(response.Data);
            } else {
                return response.Data;
            }
        }
    }
}
