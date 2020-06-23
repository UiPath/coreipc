process.env.TS_NODE_PROJECT = 'test/tsconfig.json';

const chai = require('chai');
const spies = require('chai-spies');
const { Mock } = require('ts-mockery');

chai.use(spies);

const isFn = (fn) => typeof fn === 'function';

const isSpy = (fn) => isFn(fn) &&
    !!fn.__spy

Mock.configure({
    getSpy: () => chai.spy,
    spyAndCallFake: (object, key, stub) => {
        const value = object[key];

        if (isSpy(value)) {
            chai.spy.restore(object, key);
        }

        chai.spy.on(object, key, stub);
    },
    spyAndCallThrough: (object, key) => {
        const value = object[key];

        if (!isFn(value)) { return; }

        if (isSpy(value)) {
            chai.spy.restore(object, key);
        }

        chai.spy.on(object, key);
    },
});

require('temp')
    .track();
