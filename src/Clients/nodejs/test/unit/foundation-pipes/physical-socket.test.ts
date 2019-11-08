// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import { PhysicalSocket } from '../../../src/foundation/pipes';

use(spies);

describe(`foundation:pipes -> class:PhysicalSocket`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            (() => new PhysicalSocket()).should.not.throw();
        });
        it(`deliberate failure`, () => {
            throw new Error('deliberate failure');
        });
    });
});
