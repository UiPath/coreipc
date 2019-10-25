import '@uipath/ipc';

describe(`PromisePal`, () => {

    it(`made it all the way here`, async () => {
        expect(Promise.delay).toBeInstanceOf(Function);
        const promise = Promise.delay(10);
        expect(promise).toBeInstanceOf(Promise);
        await expect(promise).resolves.toBeUndefined();
    }, 100);

});
