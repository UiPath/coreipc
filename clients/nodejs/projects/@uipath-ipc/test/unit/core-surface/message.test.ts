// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import { Message } from '@core/surface/message';

import { CancellationTokenSource, CancellationToken, TimeSpan } from '@foundation/threading';
import { ArgumentError, ArgumentNullError, OperationCanceledError, AggregateError } from '@foundation/errors';

use(spies);

describe(`core:surface -> class:Message`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw provided no args`, () => {
            (() => new Message()).should.not.throw();
        });
        it(`shouldn't throw provided a non-negative TimeSpan`, () => {
            (() => new Message(TimeSpan.fromMilliseconds(0))).should.not.throw();
            (() => new Message(TimeSpan.fromMilliseconds(1))).should.not.throw();
        });
        it(`should throw provided a negative TimeSpan`, () => {
            (() => new Message(TimeSpan.fromMilliseconds(-1))).
                should.throw(ArgumentError).
                that.has.property('maybeParamName', 'requestTimeout');
        });
        it(`shouldn't throw provided a payload`, () => {
            (() => new Message(null)).should.not.throw();
            (() => new Message(undefined)).should.not.throw();
            (() => new Message('payload')).should.not.throw();
            (() => new Message(123)).should.not.throw();
            (() => new Message(true)).should.not.throw();
        });
        it(`shouldn't throw provided a payload and a non-negative TimeSpan`, () => {
            (() => new Message(null, TimeSpan.fromMilliseconds(0))).should.not.throw();
            (() => new Message(undefined, TimeSpan.fromMilliseconds(0))).should.not.throw();
            (() => new Message('payload', TimeSpan.fromMilliseconds(0))).should.not.throw();
            (() => new Message(123, TimeSpan.fromMilliseconds(0))).should.not.throw();
            (() => new Message(true, TimeSpan.fromMilliseconds(0))).should.not.throw();

            (() => new Message(null, TimeSpan.fromMilliseconds(1))).should.not.throw();
            (() => new Message(undefined, TimeSpan.fromMilliseconds(1))).should.not.throw();
            (() => new Message('payload', TimeSpan.fromMilliseconds(1))).should.not.throw();
            (() => new Message(123, TimeSpan.fromMilliseconds(1))).should.not.throw();
            (() => new Message(true, TimeSpan.fromMilliseconds(1))).should.not.throw();
        });
        it(`should throw provided a payload and a negative TimeSpan`, () => {
            (() => new Message(null, TimeSpan.fromMilliseconds(-1))).should.throw(ArgumentError).that.has.property('maybeParamName', 'requestTimeout');
            (() => new Message(undefined, TimeSpan.fromMilliseconds(-1))).should.throw(ArgumentError).that.has.property('maybeParamName', 'requestTimeout');
            (() => new Message('payload', TimeSpan.fromMilliseconds(-1))).should.throw(ArgumentError).that.has.property('maybeParamName', 'requestTimeout');
            (() => new Message(123, TimeSpan.fromMilliseconds(-1))).should.throw(ArgumentError).that.has.property('maybeParamName', 'requestTimeout');
            (() => new Message(true, TimeSpan.fromMilliseconds(-1))).should.throw(ArgumentError).that.has.property('maybeParamName', 'requestTimeout');
        });
    });
});
