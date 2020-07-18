import { ipc, __hasName__, __test__, coreIpcContract, Message } from '@core';
import { CancellationToken } from '@foundation';
import { expect } from 'chai';

describe(`surface`, () => {
    describe(`__test__`, () => {
        it(`should work`, () => {
            @coreIpcContract
            class Foo {
                @__test__
                public async bar(x: number, y: string, z?: Message<void>, ct?: CancellationToken): Promise<Buffer> {
                    return Buffer.alloc(1);
                }
            }
        });
    });
});
