// tslint:disable: no-unused-expression

import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import { ISocketLike } from '@foundation/pipes';

use(spies);

/* @internal */
export class SocketLikeMocks {
    private static createMock(partial: Partial<ISocketLike>): ISocketLike { return partial as any; }

    public static createImmediatelyConnectingMock(): ISocketLike {
        return SocketLikeMocks.createMock({
            connect(this: ISocketLike, path: string, connectionListener: () => void): ISocketLike {
                expect(connectionListener).not.to.be.null;
                expect(connectionListener).not.to.be.undefined;
                connectionListener();
                return this;
            },
            once(this: ISocketLike, event: 'error', listener: (err: Error) => void): ISocketLike {
                return this;
            },
            addListener(this: ISocketLike, event: 'data' | 'end', listener: any): ISocketLike {
                return this;
            }
        });
    }

    public static createDelayedConnectingMock(delay: number): ISocketLike {
        return SocketLikeMocks.createMock({
            connect(this: ISocketLike, path: string, connectionListener: () => void): ISocketLike {
                expect(connectionListener).not.to.be.null;
                expect(connectionListener).not.to.be.undefined;
                if (delay === 0) {
                    connectionListener();
                } else {
                    (async () => {
                        await Promise.delay(delay);
                        connectionListener();
                    })();
                }
                return this;
            },
            once(this: ISocketLike, event: 'error', listener: (err: Error) => void): ISocketLike {
                return this;
            },
            addListener(this: ISocketLike, event: 'data' | 'end', listener: any): ISocketLike {
                return this;
            }
        });
    }

    public static creatingFailingMock(error: any): ISocketLike {
        return SocketLikeMocks.createMock({
            connect(this: ISocketLike, path: string, connectionListener: () => void): ISocketLike {
                expect(connectionListener).not.to.be.null;
                expect(connectionListener).not.to.be.undefined;
                return this;
            },
            once(this: ISocketLike, event: 'error', listener: (err: Error) => void): ISocketLike {
                listener(error as any);
                return this;
            },
            addListener(this: ISocketLike, event: 'data' | 'end', listener: any): ISocketLike {
                return this;
            }
        });
    }

    public static createDisconnectableMock(): ISocketLike {
        return SocketLikeMocks.createMock({
            connect(this: ISocketLike, path: string, connectionListener: () => void): ISocketLike {
                expect(connectionListener).not.to.be.null;
                expect(connectionListener).not.to.be.undefined;
                connectionListener();
                return this;
            },
            once(this: ISocketLike, event: 'error', listener: (err: Error) => void): ISocketLike {
                return this;
            },
            addListener(this: ISocketLike, event: 'data' | 'end', listener: any): ISocketLike {
                return this;
            },
            removeAllListeners(this: ISocketLike, event?: string | symbol): ISocketLike {
                return this;
            },
            unref(): void { },
            destroy(error?: Error): void { }
        });
    }

    public static createReadableMock(): ISocketLike {
        return SocketLikeMocks.createMock({
            connect(this: ISocketLike, path: string, connectionListener: () => void): ISocketLike {
                expect(connectionListener).not.to.be.null;
                expect(connectionListener).not.to.be.undefined;
                connectionListener();
                return this;
            },
            once(this: ISocketLike, event: 'error', listener: (err: Error) => void): ISocketLike {
                return this;
            },
            addListener(this: ISocketLike, event: 'data' | 'end', listener: (data: Buffer) => void): ISocketLike {
                return this;
            }
        });
    }

    public static createEndEmittingMock(): {
        socketLike: ISocketLike,
        emitEnd: () => void
    } {
        let endListener: any = null;
        function emitEnd(): void {
            expect(endListener).not.to.be.null;
            endListener();
        }

        const socketLike = SocketLikeMocks.createMock({
            connect(this: ISocketLike, path: string, connectionListener: () => void): ISocketLike {
                expect(connectionListener).not.to.be.null;
                expect(connectionListener).not.to.be.undefined;
                connectionListener();
                return this;
            },
            once(this: ISocketLike, event: 'error', listener: (err: Error) => void): ISocketLike {
                return this;
            },
            addListener(this: ISocketLike, event: 'data' | 'end', listener: (data: Buffer) => void): ISocketLike {
                if (event === 'end') {
                    endListener = listener as any;
                }
                return this;
            },
            removeListener(this: ISocketLike, event: 'data' | 'end', listener: (data: Buffer) => void): ISocketLike {
                return this;
            }
        });

        return { socketLike, emitEnd };
    }

    public static createEmittingMock(): {
        socketLike: ISocketLike,
        emitData: (data: Buffer) => void,
        emitEnd: () => void
    } {
        let dataListener: any = null;
        let endListener: any = null;

        function emitData(data: Buffer): void {
            expect(dataListener).not.to.be.null;
            dataListener(data);
        }
        function emitEnd(): void {
            expect(dataListener).not.to.be.null;
            endListener();
        }

        const socketLike = SocketLikeMocks.createMock({
            connect(this: ISocketLike, path: string, connectionListener: () => void): ISocketLike {
                expect(connectionListener).not.to.be.null;
                expect(connectionListener).not.to.be.undefined;
                connectionListener();
                return this;
            },
            once(this: ISocketLike, event: 'error', listener: (err: Error) => void): ISocketLike {
                return this;
            },
            addListener(this: ISocketLike, event: 'data' | 'end', listener: (data: Buffer) => void): ISocketLike {
                switch (event) {
                    case 'data':
                        dataListener = listener as any;
                        break;
                    case 'end':
                        endListener = listener as any;
                        break;
                    default:
                        throw new Chai.AssertionError(`Expecting either 'data' or 'end' but received '${event}'.`);
                }
                return this;
            },
            removeListener(this: ISocketLike, event: 'data' | 'end', listener: (data: Buffer) => void): ISocketLike {
                return this;
            },
            removeAllListeners(this: ISocketLike, event: 'data' | 'end'): ISocketLike {
                return this;
            },
            unref(): void {
            },
            destroy(error?: Error): void {
            }
        });

        return { socketLike, emitData, emitEnd };
    }

    public static createWritableMock(): ISocketLike {
        return SocketLikeMocks.createMock({
            connect(this: ISocketLike, path: string, connectionListener: () => void): ISocketLike {
                expect(connectionListener).not.to.be.null;
                expect(connectionListener).not.to.be.undefined;
                connectionListener();
                return this;
            },
            once(this: ISocketLike, event: 'error', listener: (err: Error) => void): ISocketLike {
                return this;
            },
            addListener(this: ISocketLike, event: 'data' | 'end', listener: (data: Buffer) => void): ISocketLike {
                return this;
            },
            write(buffer: Uint8Array | string, cb?: (err?: Error) => void): boolean {
                expect(cb).not.to.be.null;
                expect(cb).not.to.be.undefined;
                (async () => {
                    (cb as any)();
                })();
                return false;
            }
        });
    }
}

