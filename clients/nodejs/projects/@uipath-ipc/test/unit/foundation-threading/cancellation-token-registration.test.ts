// tslint:disable: max-line-length
// tslint:disable: no-unused-expression
import { expect, should, spy, use } from 'chai';
import spies from 'chai-spies';

import { ProperCancellationTokenRegistration, CancellationTokenRegistration, NoneCancellationTokenRegistration } from '@foundation/threading/cancellation-token-registration';
import { ProperCancellationToken } from '@foundation/threading/cancellation-token';
import { ArgumentNullError } from '@foundation/errors';

use(spies);

describe(`foundation:threading -> class:ProperCancellationTokenRegistration`, () => {

    context(`ctor`, () => {
        it(`shouldn't throw when provided a truthy ct and a truthy callback`, () => {
            const ct = new ProperCancellationToken();
            const cb = () => { };
            expect(() => new ProperCancellationTokenRegistration(ct, cb)).not.to.throw();
        });
        it(`should throw when provided a falsy ct`, () => {
            const cb = () => { };
            expect(() => new ProperCancellationTokenRegistration(null as any, cb)).to.throw(ArgumentNullError).with.property('paramName', '_cancellationToken');
            expect(() => new ProperCancellationTokenRegistration(undefined as any, cb)).to.throw(ArgumentNullError).with.property('paramName', '_cancellationToken');
        });
        it(`should throw when provided a falsy callback`, () => {
            const ct = new ProperCancellationToken();
            expect(() => new ProperCancellationTokenRegistration(ct, null as any)).to.throw(ArgumentNullError).with.property('paramName', '_callback');
            expect(() => new ProperCancellationTokenRegistration(ct, undefined as any)).to.throw(ArgumentNullError).with.property('paramName', '_callback');
        });
        it(`should throw when provided both a falsy ct and a falsy callback`, () => {
            expect(() => new ProperCancellationTokenRegistration(null as any, null as any)).to.throw(ArgumentNullError).with.property('paramName', '_cancellationToken');
            expect(() => new ProperCancellationTokenRegistration(null as any, undefined as any)).to.throw(ArgumentNullError).with.property('paramName', '_cancellationToken');
            expect(() => new ProperCancellationTokenRegistration(undefined as any, null as any)).to.throw(ArgumentNullError).with.property('paramName', '_cancellationToken');
            expect(() => new ProperCancellationTokenRegistration(undefined as any, undefined as any)).to.throw(ArgumentNullError).with.property('paramName', '_cancellationToken');
        });
    });

    context(`method:dispose`, () => {
        it(`shouldn't throw (even if called multiple times)`, () => {
            const ct = new ProperCancellationToken();
            const cb = () => { };
            const ctreg = new ProperCancellationTokenRegistration(ct, cb);

            expect(() => ctreg.dispose()).not.to.throw();
            expect(() => ctreg.dispose()).not.to.throw();
        });
        it(`should stop cancellation propagation towards the callback`, () => {
            const ct = new ProperCancellationToken();
            const cb = () => { };
            const cbSpy = spy(cb);

            const ctreg = new ProperCancellationTokenRegistration(ct, cb);
            ctreg.dispose();
            ct.cancel(false);

            expect(cbSpy).not.to.have.been.called();
        });
    });

});
describe(`foundation:threading -> class:CancellationTokenRegistration`, () => {

    context(`property:none`, () => {

        it(`shouldn't throw`, () => {
            expect(() => CancellationTokenRegistration.none).not.to.throw();
        });

        it(`should return a truthy reference`, () => {
            expect(CancellationTokenRegistration.none).not.to.be.null;
            expect(CancellationTokenRegistration.none).not.to.be.undefined;
        });

        it(`should return the same reference every time`, () => {
            expect(CancellationTokenRegistration.none).to.equal(CancellationTokenRegistration.none);
        });

    });

    context(`method:create`, () => {

        it(`shouldn't throw when provided valid args`, () => {
            const ct = new ProperCancellationToken();
            const cb = () => { };

            expect(() => CancellationTokenRegistration.create(ct, cb)).not.to.throw();
        });

        it(`should throw when provided a falsy ct`, () => {
            const cb = () => { };

            expect(() => CancellationTokenRegistration.create(null as any, cb)).to.throw();
            expect(() => CancellationTokenRegistration.create(undefined as any, cb)).to.throw();
        });

        it(`should throw when provided a falsy callback`, () => {
            const ct = new ProperCancellationToken();

            expect(() => CancellationTokenRegistration.create(ct, null as any)).to.throw();
            expect(() => CancellationTokenRegistration.create(ct, undefined as any)).to.throw();
        });

        it(`should throw when provided both a falsy cb and a falsy callback`, () => {
            expect(() => CancellationTokenRegistration.create(null as any, null as any)).to.throw();
            expect(() => CancellationTokenRegistration.create(null as any, undefined as any)).to.throw();
            expect(() => CancellationTokenRegistration.create(undefined as any, null as any)).to.throw();
            expect(() => CancellationTokenRegistration.create(undefined as any, undefined as any)).to.throw();
        });

    });

});
describe(`foundation:threading -> class:NoneCancellationTokenRegistration`, () => {
    context(`property:instance`, () => {
        it(`shouldn't throw`, () => {
            expect(() => NoneCancellationTokenRegistration.instance).not.to.throw();
        });

        it(`should return a truthy reference`, () => {
            expect(NoneCancellationTokenRegistration.instance).to.be.not.null;
            expect(NoneCancellationTokenRegistration.instance).not.to.be.undefined;
        });
    });
    context(`method:dispose`, () => {
        it(`shouldn't throw`, () => {
            expect(() => NoneCancellationTokenRegistration.instance.dispose()).not.to.throw();
        });
    });
});
