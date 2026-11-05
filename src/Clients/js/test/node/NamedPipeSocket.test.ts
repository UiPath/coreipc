import * as net from 'net';

import { expect } from 'chai';

import { CancellationToken, TimeSpan } from '../../src/std';
import { NamedPipeSocket, NamedPipeSocketLike, Platform } from '../../src/node';

class FakeSocketLike implements NamedPipeSocketLike {
    public static failures = 0;
    public static code = 'ENOENT';
    public static attempts = 0;

    private _onError?: (error: Error) => void;

    public once(_event: 'error', listener: (error: Error) => void): this {
        this._onError = listener;
        return this;
    }
    public on(): this {
        return this;
    }
    public connect(_path: string, connectionListener?: () => void): this {
        const attempt = ++FakeSocketLike.attempts;
        setImmediate(() => {
            if (attempt <= FakeSocketLike.failures) {
                this._onError?.(Object.assign(new Error('pipe not there'), { code: FakeSocketLike.code }));
            } else {
                connectionListener?.();
            }
        });
        return this;
    }
    public write(): boolean {
        return true;
    }
    public removeAllListeners(): this {
        return this;
    }
    public unref(): void {}
    public destroy(): void {}

    public static reset(failures: number, code = 'ENOENT') {
        FakeSocketLike.failures = failures;
        FakeSocketLike.code = code;
        FakeSocketLike.attempts = 0;
    }
}

describe(`NamedPipeSocket.connectRetrying`, () => {
    const timeout = TimeSpan.fromSeconds(5);

    it(`retries a missing pipe until it appears`, async () => {
        FakeSocketLike.reset(3);

        const socket = await NamedPipeSocket.connectRetrying('p', timeout, CancellationToken.none, FakeSocketLike);

        expect(FakeSocketLike.attempts).to.equal(4);
        socket.dispose();
    });

    it(`retries a refused connection`, async () => {
        FakeSocketLike.reset(2, 'ECONNREFUSED');

        const socket = await NamedPipeSocket.connectRetrying('p', timeout, CancellationToken.none, FakeSocketLike);

        expect(FakeSocketLike.attempts).to.equal(3);
        socket.dispose();
    });

    it(`does not retry any other error`, async () => {
        FakeSocketLike.reset(Number.POSITIVE_INFINITY, 'EACCES');

        const error = await NamedPipeSocket.connectRetrying('p', timeout, CancellationToken.none, FakeSocketLike).then(
            () => undefined,
            (e) => e,
        );

        expect(error?.code).to.equal('EACCES');
        expect(FakeSocketLike.attempts).to.equal(1);
    });

    it(`gives up at the timeout`, async () => {
        FakeSocketLike.reset(Number.POSITIVE_INFINITY);
        const started = Date.now();

        const error = await NamedPipeSocket.connectRetrying(
            'p',
            TimeSpan.fromMilliseconds(300),
            CancellationToken.none,
            FakeSocketLike,
        ).then(
            () => undefined,
            (e) => e,
        );

        expect(error).to.not.be.undefined;
        expect(Date.now() - started).to.be.lessThan(2000);
        expect(FakeSocketLike.attempts).to.be.greaterThan(4);
    });

    it(`connects to a real pipe whose server starts listening later`, async () => {
        const pipeName = `coreipc-retry-${Math.random().toString(16).slice(2)}`;
        const server = net.createServer((connection) => connection.end());
        const listening = new Promise<void>((resolve) =>
            setTimeout(() => server.listen(Platform.current.getFullPipeName(pipeName), resolve), 500),
        );

        try {
            const socket = await NamedPipeSocket.connectRetrying(pipeName, timeout, CancellationToken.none);
            socket.dispose();
        } finally {
            await listening;
            await new Promise<void>((resolve) => server.close(() => resolve()));
        }
    });
});
