import { expect, concatArgs, calling } from '@test-helpers';
import { Marshal, ArgumentOutOfRangeError } from '@foundation';

describe(`internals`, () => {
    describe(`Marshal`, () => {
        context(`the sizeOf method`, () => {
            it(`should return 4 for 'int32be'`, () => {
                expect(Marshal.sizeOf('int32be')).to.be.eq(4);
            });

            it(`should return 1 for 'uint8'`, () => {
                expect(Marshal.sizeOf('uint8')).to.be.eq(1);
            });

            it(`should throw for anything other than 'int32be' or 'uint8'`, () => {
                calling(Marshal.sizeOf, 'foobar' as any).should.throw(ArgumentOutOfRangeError);
            });
        });
    });
});
