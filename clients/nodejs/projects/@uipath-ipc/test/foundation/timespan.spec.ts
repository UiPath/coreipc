import '../jest-extensions';
import { TimeSpan } from '../../src/foundation/tasks/timespan';

describe('Foundation-TimeSpan', () => {

    test(`fromMilliseconds doesn't throw`, () => {
        expect(() => TimeSpan.fromMilliseconds(0)).not.toThrow();
        expect(() => TimeSpan.fromMilliseconds(75)).not.toThrow();
    });
    test(`fromSeconds doesn't throw`, () => {
        expect(() => TimeSpan.fromSeconds(0)).not.toThrow();
        expect(() => TimeSpan.fromSeconds(75)).not.toThrow();
    });
    test(`fromMinutes doesn't throw`, () => {
        expect(() => TimeSpan.fromMinutes(0)).not.toThrow();
        expect(() => TimeSpan.fromMinutes(75)).not.toThrow();
    });
    test(`fromHours doesn't throw`, () => {
        expect(() => TimeSpan.fromHours(0)).not.toThrow();
    });
    test(`fromDays doesn't throw`, () => {
        expect(() => TimeSpan.fromDays(0)).not.toThrow();
        expect(() => TimeSpan.fromDays(75)).not.toThrow();
    });

    test(`toString works`, () => {
        expect(TimeSpan.fromMilliseconds(0).toString()).toEqual('00:00:00');
        expect(TimeSpan.fromMilliseconds(1).toString()).toEqual('00:00:00.001');
        expect(TimeSpan.fromMilliseconds(2).toString()).toEqual('00:00:00.002');
        expect(TimeSpan.fromSeconds(1).toString())     .toEqual('00:00:01');
        expect(TimeSpan.fromSeconds(2).toString())     .toEqual('00:00:02');
        expect(TimeSpan.fromMinutes(1).toString())     .toEqual('00:01:00');
        expect(TimeSpan.fromMinutes(2).toString())     .toEqual('00:02:00');
        expect(TimeSpan.fromHours(1).toString())       .toEqual('01:00:00');
        expect(TimeSpan.fromHours(2).toString())       .toEqual('02:00:00');
        expect(TimeSpan.fromDays(1).toString())        .toEqual('1.00:00:00');
        expect(TimeSpan.fromDays(2).toString())        .toEqual('2.00:00:00');
        expect(TimeSpan.fromHours(25).toString())      .toEqual('1.01:00:00');
        expect(TimeSpan.fromHours(51).toString())      .toEqual('2.03:00:00');

        expect(TimeSpan.zero.subtract(TimeSpan.fromMilliseconds(0)).toString()).toEqual('00:00:00');
        expect(TimeSpan.zero.subtract(TimeSpan.fromMilliseconds(1)).toString()).toEqual('-00:00:00.001');
        expect(TimeSpan.zero.subtract(TimeSpan.fromMilliseconds(2)).toString()).toEqual('-00:00:00.002');
        expect(TimeSpan.zero.subtract(TimeSpan.fromSeconds(1)     ).toString()).toEqual('-00:00:01');
        expect(TimeSpan.zero.subtract(TimeSpan.fromSeconds(2)     ).toString()).toEqual('-00:00:02');
        expect(TimeSpan.zero.subtract(TimeSpan.fromMinutes(1)     ).toString()).toEqual('-00:01:00');
        expect(TimeSpan.zero.subtract(TimeSpan.fromMinutes(2)     ).toString()).toEqual('-00:02:00');
        expect(TimeSpan.zero.subtract(TimeSpan.fromHours(1)       ).toString()).toEqual('-01:00:00');
        expect(TimeSpan.zero.subtract(TimeSpan.fromHours(2)       ).toString()).toEqual('-02:00:00');
        expect(TimeSpan.zero.subtract(TimeSpan.fromDays(1)        ).toString()).toEqual('-1.00:00:00');
        expect(TimeSpan.zero.subtract(TimeSpan.fromDays(2)        ).toString()).toEqual('-2.00:00:00');
        expect(TimeSpan.zero.subtract(TimeSpan.fromHours(25)      ).toString()).toEqual('-1.01:00:00');
        expect(TimeSpan.zero.subtract(TimeSpan.fromHours(51)      ).toString()).toEqual('-2.03:00:00');
    });

    test('zero and isZero work', () => {
        expect(() => TimeSpan.zero).not.toThrow();
        expect(() => TimeSpan.zero.isZero).not.toThrow();
        expect(() => TimeSpan.fromDays(1).isZero).not.toThrow();

        expect(TimeSpan.zero).toBeMatchedBy<TimeSpan>(value => value.isZero);
        expect(TimeSpan.fromDays(1)).toBeMatchedBy<TimeSpan>(value => !value.isZero);
    });
    test('isPositive and isNegative work', () => {
        expect(() => TimeSpan.zero.isPositive).not.toThrow();
        expect(() => TimeSpan.zero.isNegative).not.toThrow();

        expect(() => TimeSpan.fromDays(1).isPositive).not.toThrow();
        expect(() => TimeSpan.fromDays(1).isNegative).not.toThrow();

        expect(TimeSpan.zero).toBeMatchedBy<TimeSpan>(value => value.isPositive);
        expect(TimeSpan.zero).toBeMatchedBy<TimeSpan>(value => !value.isNegative);

        expect(TimeSpan.fromDays(1)).toBeMatchedBy<TimeSpan>(value => value.isPositive);
        expect(TimeSpan.fromDays(1)).toBeMatchedBy<TimeSpan>(value => !value.isNegative);

        expect(TimeSpan.fromDays(-1)).toBeMatchedBy<TimeSpan>(value => !value.isPositive);
        expect(TimeSpan.fromDays(-1)).toBeMatchedBy<TimeSpan>(value => value.isNegative);
    });
    test('add and subtract work', () => {
        const values = [TimeSpan.zero, TimeSpan.fromDays(1), TimeSpan.fromMinutes(-1)];
        for (const x of values) {
            for (const y of values) {
                expect(() => x.add(y)).not.toThrow();
                expect(() => x.subtract(y)).not.toThrow();
            }
        }

        expect(TimeSpan.zero.add(TimeSpan.fromDays(1))).toBeMatchedBy<TimeSpan>(value => value.totalDays === 1);
        expect(TimeSpan.zero.subtract(TimeSpan.fromMinutes(1))).toBeMatchedBy<TimeSpan>(value => value.totalMinutes === -1);
    });

    test('consistency', () => {
        let xs = [
            TimeSpan.fromMilliseconds(0),
            TimeSpan.fromSeconds(0),
            TimeSpan.fromMinutes(0),
            TimeSpan.fromHours(0),
            TimeSpan.fromDays(0)
        ];
        for (const x of xs) {
            expect(x.milliseconds).toBe(0);
            expect(x.seconds).toBe(0);
            expect(x.minutes).toBe(0);
            expect(x.hours).toBe(0);
            expect(x.days).toBe(0);
            expect(x.totalMilliseconds).toBe(0);
            expect(x.totalSeconds).toBe(0);
            expect(x.totalMinutes).toBe(0);
            expect(x.totalHours).toBe(0);
            expect(x.totalDays).toBe(0);
            expect(x.toString()).toBe('00:00:00');
        }

        xs = [
            TimeSpan.fromMilliseconds(900),
            TimeSpan.fromSeconds(0.9),
            TimeSpan.fromMinutes(0.9 / 60),
            TimeSpan.fromHours(0.9 / 3600),
            TimeSpan.fromDays(0.9 / 86400)
        ];
        for (const x of xs) {
            expect(x.milliseconds).toBe(900);
            expect(x.seconds).toBe(0);
            expect(x.minutes).toBe(0);
            expect(x.hours).toBe(0);
            expect(x.days).toBe(0);
            expect(x.totalMilliseconds).toBeCloseTo(900, 6);
            expect(x.totalSeconds).toBeCloseTo(0.9, 6);
            expect(x.totalMinutes).toBeCloseTo(0.9 / 60, 6);
            expect(x.totalHours).toBeCloseTo(0.9 / 3600, 6);
            expect(x.totalDays).toBeCloseTo(0.9 / 86400, 6);
            expect(x.toString()).toBe('00:00:00.900');
        }

        xs = [
            TimeSpan.fromMilliseconds(1000),
            TimeSpan.fromSeconds(1),
            TimeSpan.fromMinutes(1 / 60),
            TimeSpan.fromHours(1 / 3600),
            TimeSpan.fromDays(1 / 86400)
        ];
        for (const x of xs) {
            expect(x.milliseconds).toBe(0);
            expect(x.seconds).toBe(1);
            expect(x.minutes).toBe(0);
            expect(x.hours).toBe(0);
            expect(x.days).toBe(0);
            expect(x.totalMilliseconds).toBe(1000);
            expect(x.totalSeconds).toBe(1);
            expect(x.totalMinutes).toBe(1 / 60);
            expect(x.totalHours).toBe(1 / (60 * 60));
            expect(x.totalDays).toBe(1 / (60 * 60 * 24));
            expect(x.toString()).toBe('00:00:01');
        }

        const days = 2.34;
        xs = [
            TimeSpan.fromMilliseconds(days * 86400 * 1000),
            TimeSpan.fromSeconds(days * 86400),
            TimeSpan.fromMinutes(days * 1440),
            TimeSpan.fromHours(days * 24),
            TimeSpan.fromDays(days)
        ];
        for (const x of xs) {
            expect(x.milliseconds).toBe(Math.floor(days * 86400 * 1000) % 1000);
            expect(x.seconds).toBe(Math.floor(days * 86400) % 60);
            expect(x.minutes).toBe(Math.floor(days * 1440) % 60);
            expect(x.hours).toBe(Math.floor(days * 24) % 24);
            expect(x.days).toBe(Math.floor(days));
            expect(x.totalMilliseconds).toBe(days * 86400 * 1000);
            expect(x.totalSeconds).toBe(days * 86400);
            expect(x.totalMinutes).toBe(days * 1440);
            expect(x.totalHours).toBe(days * 24);
            expect(x.totalDays).toBe(days);
            expect(x.toString()).toBe('2.08:09:36');
        }

        xs = [
            TimeSpan.fromMilliseconds(1 * 86400 * 1000),
            TimeSpan.fromSeconds(1 * 86400),
            TimeSpan.fromMinutes(1 * 1440),
            TimeSpan.fromHours(1 * 24),
            TimeSpan.fromDays(1)
        ];
        for (const x of xs) {
            expect(x.milliseconds).toBe(0);
            expect(x.seconds).toBe(0);
            expect(x.minutes).toBe(0);
            expect(x.hours).toBe(0);
            expect(x.days).toBe(1);
            expect(x.totalMilliseconds).toBe(1 * 86400 * 1000);
            expect(x.totalSeconds).toBe(1 * 86400);
            expect(x.totalMinutes).toBe(1 * 1440);
            expect(x.totalHours).toBe(1 * 24);
            expect(x.totalDays).toBe(1);
            expect(x.toString()).toBe('1.00:00:00');
        }

    });

});
