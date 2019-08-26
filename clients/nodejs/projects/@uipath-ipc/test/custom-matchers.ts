// tslint:disable: no-namespace
// tslint:disable: ban-types

export {};

declare global {
    namespace jest {
        interface Matchers<R> {
            toThrowSubClassOf(errorCtor: Function): R;
            toThrowPrecisely(errorCtor: Function): R;
        }
    }
}

expect.extend({
    toThrowSubClassOf(received: () => any, errorCtor: Function) {
        try {
            received();
            return {
                message: () => `Should have thrown ${errorCtor.name} but didn't throw anything.`,
                pass: false
            };
        } catch (actual) {
            if (actual instanceof errorCtor) {
                return {
                    message: () => '',
                    pass: true
                };
            } else {
                return {
                    message: () => `Should have thrown ${errorCtor.name} but threw ${actual.constructor.name}.`,
                    pass: false
                };
            }
        }
    },
    toThrowPrecisely(received: () => any, errorCtor: Function) {
        try {
            received();
            return {
                message: () => `Should have thrown ${errorCtor.name} but didn't throw anything.`,
                pass: false
            };
        } catch (actual) {
            if (actual.constructor === errorCtor) {
                return {
                    message: () => '',
                    pass: true
                };
            } else {
                return {
                    message: () => `Should have thrown ${errorCtor.name} but threw ${actual.constructor.name}.`,
                    pass: false
                };
            }
        }
    }
});
