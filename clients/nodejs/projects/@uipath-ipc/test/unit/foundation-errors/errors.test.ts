// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, spy, use } from 'chai';
import 'chai/register-should';
import spies from 'chai-spies';

import * as errors from '@foundation/errors';

use(spies);

describe(`foundation:errors -> class:AbstractMemberError`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw provided no args`, () => {
            (() => new errors.AbstractMemberError()).should.not.throw();
        });
        it(`shouldn't throw provided 1 arg`, () => {
            (() => new errors.AbstractMemberError('message')).should.not.throw();
        });
        it(`shouldn't throw provided 2 args`, () => {
            (() => new errors.AbstractMemberError('message', 'member-name')).should.not.throw();
        });

        it(`should set the "message" to the default value and leave "maybeMemberName" undefined provided no args`, () => {
            const error = new errors.AbstractMemberError();
            expect(error.message).to.equal(errors.AbstractMemberError.defaultMessage);
        });

        it(`should leave "maybeMemberName" undefined provided no args`, () => {
            const error = new errors.AbstractMemberError();
            expect(error.maybeMemberName).to.be.undefined;
        });

        it(`should set the "message" to the arg's value provided 1 arg`, () => {
            const error = new errors.AbstractMemberError('message');
            expect(error.message).to.equal('message');
        });

        it(`should leave "maybeMemberName" undefined provided 1 arg`, () => {
            const error = new errors.AbstractMemberError('message');
            expect(error.maybeMemberName).to.be.undefined;
        });

        it(`should compute "message" from both args`, () => {
            const error = new errors.AbstractMemberError('message', 'member-name');
            expect(error.message).to.equal(errors.AbstractMemberError.computeMessage('message', 'member-name'));
        });

        it(`should set "maybeMembers" as the 2nd arg provided 2 args`, () => {
            const error = new errors.AbstractMemberError('message', 'member-name');
            expect(error.maybeMemberName).to.equal('member-name');
        });
    });
});

describe(`foundation:errors -> class:AggregateError`, () => {
    const error1 = new Error();
    const error2 = new Error();
    const error3 = new Error();

    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            (() => new errors.AggregateError()).should.not.throw();
            (() => new errors.AggregateError('message')).should.not.throw();
            (() => new errors.AggregateError(error1, error2, error3)).should.not.throw();
            (() => new errors.AggregateError('message', error1, error2, error3)).should.not.throw();
        });

        it(`should splice the 1st arg into the "errors" collection provided it's an Error`, () => {
            const error = new errors.AggregateError(error1, error2, error3);

            error.errors.length.should.be.equal(3);
            error.errors[0].should.be.equal(error1);
            error.errors[1].should.be.equal(error2);
            error.errors[2].should.be.equal(error3);
        });

        it(`should set an empty "errors" collection provided no args`, () => {
            const error = new errors.AggregateError();

            expect(error.errors).to.eql([]);
        });

        it(`should leave the default "message" provided the 1st arg is an Error`, () => {
            const error = new errors.AggregateError(error1, error2, error3);
            expect(error.message).to.equal(errors.AggregateError.defaultMessage);
        });

        it(`should leave the default "message" provided no args`, () => {
            const error = new errors.AggregateError();
            expect(error.message).to.equal(errors.AggregateError.defaultMessage);
        });

        it(`should set "message" to the 1st arg's value provided it's not an Error`, () => {
            const error = new errors.AggregateError('message', error2, error3);
            error.message.should.equal('message');
        });

        it(`should set an empty "errors" collection provided 1 arg which isn't an Error`, () => {
            const error = new errors.AggregateError('message');
            expect(error.errors).to.eql([]);
        });

        it(`should set the "errors" collection as the last N-1 args provided the 1st arg isn't an Error`, () => {
            const error = new errors.AggregateError('message', error2, error3);

            error.errors.length.should.equal(2);
            error.errors[0].should.equal(error2);
            error.errors[1].should.equal(error3);
        });
    });
});

describe(`foundation:errors -> class:ArgumentNullError`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw provided no args`, () => {
            (() => new errors.ArgumentNullError()).should.not.throw();
        });
        it(`shouldn't throw provided 1 arg`, () => {
            (() => new errors.ArgumentNullError('message')).should.not.throw();
        });
        it(`shouldn't throw provided 2 args`, () => {
            (() => new errors.ArgumentNullError('message', 'param-name')).should.not.throw();
        });

        it(`should leave "maybeParamName" undefined provided no args`, () => {
            expect(new errors.ArgumentNullError().maybeParamName).to.be.undefined;
        });
        it(`should leave "message" the default value provided no args`, () => {
            expect(new errors.ArgumentNullError().message).to.equal(errors.ArgumentNullError.defaultMessage);
        });

        it(`should set "maybeParamName" to the arg's value provided 1 arg`, () => {
            expect(new errors.ArgumentNullError('param-name').maybeParamName).to.equal('param-name');
        });
        it(`should compute "message" from the default message and the specified "maybeParamName" provided 1 arg`, () => {
            expect(new errors.ArgumentNullError('param-name').message).to.equal(errors.ArgumentNullError.computeMessage('param-name', errors.ArgumentNullError.defaultMessage));
        });

        it(`should set "maybeParamName" to the 1st arg's value provided 2 args`, () => {
            expect(new errors.ArgumentNullError('param-name', 'message').maybeParamName).to.equal('param-name');
        });

        it(`should compute "message" from both args`, () => {
            expect(new errors.ArgumentNullError('param-name', 'message').message).to.equal(errors.ArgumentNullError.computeMessage('param-name', 'message'));
        });
    });
});

describe(`foundation:errors -> class:ArgumentError`, () => {
    context(`ctor`, () => {
        it(`shouldn't throw`, () => {
            (() => new errors.ArgumentError()).should.not.throw();
            (() => new errors.ArgumentError('param-name')).should.not.throw();
            (() => new errors.ArgumentError('param-name', 'message')).should.not.throw();
        });
    });
});
