import { concatArgs, calling, expect, asParamsOf } from '@test-helpers';
import { BitConverter, ArgumentOutOfRangeError } from '@foundation';

describe(`internals`, () => {
    describe(`BitConverter`, () => {
        context(`the getBytes method`, () => {
            it(`should throw for anything other than 'int32le' or 'uint8'`, () => {
                calling(BitConverter.getBytes, 123, 'foobar' as any).should.throw(ArgumentOutOfRangeError);
            });

            function makeArgs(...args: any[]): any { return args; }

            for (const _case of [
                { input: makeArgs(0, 'uint8'), expected: [0] },
                { input: makeArgs(1, 'uint8'), expected: [1] },
                { input: makeArgs(127, 'uint8'), expected: [127] },
                { input: makeArgs(128, 'uint8'), expected: [128] },
                { input: makeArgs(129, 'uint8'), expected: [129] },
                { input: makeArgs(255, 'uint8'), expected: [255] },

                { input: makeArgs(-2, 'int32le'), expected: [254, 255, 255, 255] },
                { input: makeArgs(-1, 'int32le'), expected: [255, 255, 255, 255] },
                { input: makeArgs(0, 'int32le'), expected: [0, 0, 0, 0] },
                { input: makeArgs(1, 'int32le'), expected: [1, 0, 0, 0] },
                { input: makeArgs(256, 'int32le'), expected: [0, 1, 0, 0] },
                { input: makeArgs(65536, 'int32le'), expected: [0, 0, 1, 0] },
                { input: makeArgs(67305985, 'int32le'), expected: [1, 2, 3, 4] },
            ]) {
                it(`(${concatArgs(_case.input)}) should return [ ${concatArgs(_case.expected)} ]`, () => {
                    expect((BitConverter.getBytes as any)(..._case.input)).to.be.deep.eq(Buffer.from(_case.expected));
                });
            }
        });

        context(`the getNumber method`, () => {
            it(`should throw for anything other than 'int32le' or 'uint8'`, () => {
                calling(BitConverter.getNumber, Buffer.alloc(4), 'foobar' as any).should.throw(ArgumentOutOfRangeError);
            });

            function makeArgs(...args: any[]): any { return args; }

            for (const _case of [
                { input: makeArgs(0, 'uint8'), expected: [0] },
                { input: makeArgs(1, 'uint8'), expected: [1] },
                { input: makeArgs(127, 'uint8'), expected: [127] },
                { input: makeArgs(128, 'uint8'), expected: [128] },
                { input: makeArgs(129, 'uint8'), expected: [129] },
                { input: makeArgs(255, 'uint8'), expected: [255] },

                { input: makeArgs(-2, 'int32le'), expected: [254, 255, 255, 255] },
                { input: makeArgs(-1, 'int32le'), expected: [255, 255, 255, 255] },
                { input: makeArgs(0, 'int32le'), expected: [0, 0, 0, 0] },
                { input: makeArgs(1, 'int32le'), expected: [1, 0, 0, 0] },
                { input: makeArgs(256, 'int32le'), expected: [0, 1, 0, 0] },
                { input: makeArgs(65536, 'int32le'), expected: [0, 0, 1, 0] },
                { input: makeArgs(67305985, 'int32le'), expected: [1, 2, 3, 4] },
            ]) {
                it(`(${concatArgs(_case.input)}) should return [ ${concatArgs(_case.expected)} ]`, () => {
                    expect((BitConverter.getBytes as any)(..._case.input)).to.be.deep.eq(Buffer.from(_case.expected));
                });
            }
        });
    });
});
