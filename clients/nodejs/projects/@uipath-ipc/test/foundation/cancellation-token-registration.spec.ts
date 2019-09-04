import { CancellationTokenRegistration } from '../../src/foundation/tasks/cancellation-token-registration';

describe('Foundation-CancellationTokenRegistration', () => {

    test(`none doesn't throw`, () => {
        expect(() => CancellationTokenRegistration.none).not.toThrow();
    });
    test(`none.dispose() doesn't throw`, () => {
        expect(() => CancellationTokenRegistration.none.dispose()).not.toThrow();
    });

});
