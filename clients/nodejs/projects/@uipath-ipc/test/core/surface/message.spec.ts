import { Message } from '../../../src/core/surface/message';
import { TimeSpan } from '../../../src/foundation/tasks/timespan';

describe('Core-Surface-Message', () => {

    test(`ctor doesn't throw`, () => {
        expect(() => new Message<void>(TimeSpan.fromSeconds(100))).not.toThrow();
        expect(() => new Message<void>(undefined, TimeSpan.fromSeconds(100))).not.toThrow();
        expect(() => new Message<string>(TimeSpan.fromSeconds(100))).not.toThrow();
        expect(() => new Message<string>('test', TimeSpan.fromSeconds(100))).not.toThrow();

        const cases: Array<{
            factory: () => Message<unknown>,
            expectedPayload: any,
            expectedTimeout: any
        }> = [
                { factory: () => new Message<void>(), expectedPayload: undefined, expectedTimeout: null },
                { factory: () => new Message<void>(null as any), expectedPayload: null, expectedTimeout: null },
                { factory: () => new Message<void>(null as any, null as any), expectedPayload: null, expectedTimeout: null },
                { factory: () => new Message<void>(undefined as any), expectedPayload: undefined, expectedTimeout: null },
                { factory: () => new Message<void>(undefined as any, undefined as any), expectedPayload: undefined, expectedTimeout: null },
                { factory: () => new Message<boolean>(true as any), expectedPayload: true, expectedTimeout: null },
                { factory: () => new Message<boolean>(true as any, TimeSpan.fromSeconds(10)), expectedPayload: true, expectedTimeout: TimeSpan.fromSeconds(10) }
            ];

        for (const _case of cases) {
            let message: Message<unknown> | null = null;
            expect(() => message = _case.factory()).not.toThrow();
            expect(message).not.toBeFalsy();
            expect(message.Payload).toBe(_case.expectedPayload);
            expect(message.RequestTimeout).toEqual(_case.expectedTimeout);
        }
    });

    const mockTimeout = TimeSpan.fromSeconds(100);
    const mockPayload = 'mock-payload';

    test(`TimeoutSeconds gets populated`, () => {
        expect(new Message<void>(mockTimeout).RequestTimeout).toBe(mockTimeout);
        expect(new Message<void>(undefined, mockTimeout).RequestTimeout).toBe(mockTimeout);

        expect(new Message<string>(mockTimeout).RequestTimeout).toBe(mockTimeout);
        expect(new Message<string>(mockPayload, mockTimeout).RequestTimeout).toBe(mockTimeout);
    });

    test(`Payload gets populated`, () => {
        expect(new Message<void>(mockTimeout).Payload).toBeUndefined();
        expect(new Message<void>(undefined, mockTimeout).Payload).toBeUndefined();

        expect(new Message<string>(mockTimeout).Payload).toBeUndefined();
        expect(new Message<string>(mockPayload, mockTimeout).Payload).toBe(mockPayload);
    });

});
