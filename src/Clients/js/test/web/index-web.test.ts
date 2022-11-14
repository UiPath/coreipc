import { expect } from 'chai';

describe('web', () => {
    beforeEach(function () {
        if (global.process?.versions?.node) {
            this.skip();
        }
    });

    it('1 should equal 1', function () {
        const act = () => eval(`new WebSocket('ws://localhost:1234', 'foobar')`)
        expect(act).not.to.throw();
    });

    it('2 should equal 2', function () {
        expect(2).to.equal(2);
    });
});


