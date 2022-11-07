import {
    NamedPipeSocketAddress,
    NamedPipeClientSocket,
} from '@foundation'

import { nameof, ctor } from '@helpers';

ctor(NamedPipeSocketAddress)
    .withArgs('some-pipe')
    .withArgs('some-pipe', undefined)
    .withArgs('some-pipe', NamedPipeClientSocket as any)
    .forEach(args => {
        test(`${nameof(NamedPipeSocketAddress)}(${args}) should not throw`, () => {
            const act = () => new NamedPipeSocketAddress(...args);
            expect(act).not.toThrow();
        });
    });



