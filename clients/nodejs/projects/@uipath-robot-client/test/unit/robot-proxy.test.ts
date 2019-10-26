// tslint:disable: max-line-length
// tslint:disable: no-unused-expression

import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import { RobotProxy } from '../../src/robot-proxy';

use(spies);

describe(`class:RobotProxy`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            (() => new RobotProxy()).should.not.throw();
        });
    });
});
