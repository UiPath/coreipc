import { Message } from '../../../src/core/surface/message';

describe('Core-Surface-Message', () => {

    test(`ctor doesn't throw`, () => {
        expect(() => new Message<void>(100)).not.toThrow();
        expect(() => new Message<void>(undefined, 100)).not.toThrow();
        expect(() => new Message<string>(100)).not.toThrow();
        expect(() => new Message<string>('test', 100)).not.toThrow();

        const cases: Array<() => Message<unknown>> = [
            () => new Message<void>(null as any),
            () => new Message<void>(null as any, null as any),
            () => new Message<void>(undefined as any),
            () => new Message<void>(undefined as any, undefined as any),
            () => new Message<void>(true as any),
            () => new Message<void>(true as any, true as any)
        ];

        for (const _case of cases) {
            let message: Message<unknown> | null = null;
            expect(() => message = _case()).not.toThrow();
            expect(message).not.toBeFalsy();
            expect(message.Payload).toBeUndefined();
            expect(message.TimeoutSeconds).toBeUndefined();
        }
    });

    const mockTimeoutSeconds = 100;
    const mockPayload = 'mock-payload';

    test(`TimeoutSeconds gets populated`, () => {
        expect(new Message<void>(mockTimeoutSeconds).TimeoutSeconds).toBe(mockTimeoutSeconds);
        expect(new Message<void>(undefined, mockTimeoutSeconds).TimeoutSeconds).toBe(mockTimeoutSeconds);

        expect(new Message<string>(mockTimeoutSeconds).TimeoutSeconds).toBe(mockTimeoutSeconds);
        expect(new Message<string>(mockPayload, mockTimeoutSeconds).TimeoutSeconds).toBe(mockTimeoutSeconds);
    });

    test(`Payload gets populated`, () => {
        expect(new Message<void>(mockTimeoutSeconds).Payload).toBeUndefined();
        expect(new Message<void>(undefined, mockTimeoutSeconds).Payload).toBeUndefined();

        expect(new Message<string>(mockTimeoutSeconds).Payload).toBeUndefined();
        expect(new Message<string>(mockPayload, mockTimeoutSeconds).Payload).toBe(mockPayload);
    });

});
