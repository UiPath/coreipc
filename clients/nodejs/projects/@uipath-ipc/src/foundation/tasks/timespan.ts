// tslint:disable-next-line: no-namespace
namespace MillisecondsInA {
    export const second = 1000;
    export const minute = 60 * second;
    export const hour = 60 * minute;
    export const day = 24 * hour;
}
export class TimeSpan {
    public add(other: TimeSpan): TimeSpan { return TimeSpan.fromMilliseconds(this._milliseconds + other._milliseconds); }
    public subtract(other: TimeSpan): TimeSpan { return TimeSpan.fromMilliseconds(this._milliseconds - other._milliseconds); }
    public static readonly zero = TimeSpan.fromMilliseconds(0);
    public get isZero(): boolean { return this._milliseconds === 0; }
    public get isPositive(): boolean { return this._milliseconds >= 0; }
    public get isNegative(): boolean { return this._milliseconds < 0; }

    public static fromMilliseconds(milliseconds: number): TimeSpan {
        return new TimeSpan(milliseconds);
    }
    public static fromSeconds(seconds: number): TimeSpan {
        return new TimeSpan(seconds * MillisecondsInA.second);
    }
    public static fromMinutes(minutes: number): TimeSpan {
        return new TimeSpan(minutes * MillisecondsInA.minute);
    }
    public static fromHours(hours: number): TimeSpan {
        return new TimeSpan(hours * MillisecondsInA.hour);
    }
    public static fromDays(days: number): TimeSpan {
        return new TimeSpan(days * MillisecondsInA.day);
    }

    private get absoluteMilliseconds(): number { return Math.abs(this._milliseconds); }

    public get totalMilliseconds(): number { return this._milliseconds; }
    public get totalSeconds(): number { return this._milliseconds / MillisecondsInA.second; }
    public get totalMinutes(): number { return this._milliseconds / MillisecondsInA.minute; }
    public get totalHours(): number { return this._milliseconds / MillisecondsInA.hour; }
    public get totalDays(): number { return this._milliseconds / MillisecondsInA.day; }

    public get days(): number { return Math.sign(this._milliseconds) * Math.floor(this.absoluteMilliseconds / MillisecondsInA.day); }
    public get hours(): number { return Math.sign(this._milliseconds) * Math.floor((this.absoluteMilliseconds % MillisecondsInA.day) / MillisecondsInA.hour); }
    public get minutes(): number { return Math.sign(this._milliseconds) * Math.floor((this.absoluteMilliseconds % MillisecondsInA.hour) / MillisecondsInA.minute); }
    public get seconds(): number { return Math.sign(this._milliseconds) * Math.floor((this.absoluteMilliseconds % MillisecondsInA.minute) / MillisecondsInA.second); }
    public get milliseconds(): number { return Math.sign(this._milliseconds) * Math.floor(this.absoluteMilliseconds % MillisecondsInA.second); }

    private constructor(public readonly _milliseconds: number) { }

    private _maybeToString: string | null = null;
    private computeToString(): string {
        function* enumerateComponents(timeSpan: TimeSpan): Iterable<string> {
            if (timeSpan._milliseconds < 0) { yield '-'; }
            if (timeSpan.days) { yield `${Math.abs(timeSpan.days)}.`; }
            yield `${Math.abs(timeSpan.hours)}`.padStart(2, '0');
            yield ':';
            yield `${Math.abs(timeSpan.minutes)}`.padStart(2, '0');
            yield ':';
            yield `${Math.abs(timeSpan.seconds)}`.padStart(2, '0');
            if (timeSpan.milliseconds) {
                yield '.';
                yield `${Math.abs(timeSpan.milliseconds)}`.padStart(3, '0');
            }
        }

        let result = '';
        for (const component of enumerateComponents(this)) {
            result += component;
        }
        return result;
    }
    public toString(): string { return this._maybeToString || (this._maybeToString = this.computeToString()); }
    public toJSON(): any { return this.toString(); }
}
