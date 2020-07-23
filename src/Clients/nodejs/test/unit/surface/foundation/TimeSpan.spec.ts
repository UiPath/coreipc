import { expect } from '@test-helpers';
import { TimeSpan, ArgumentOutOfRangeError } from '@foundation';

describe(`surface:foundation`, () => {
    describe(`TimeSpan`, () => {
        const _cases: Array<{ arg: number | null | undefined, isInvalid?: boolean }> = [
            { arg: 0 },
            { arg: 1 },
            { arg: 2 },
            { arg: 3 },
            { arg: 4 },
            { arg: -1 },
            { arg: -2 },
            { arg: -3 },
            { arg: -4 },
            { arg: null, isInvalid: true },
            { arg: undefined, isInvalid: true },
        ];
        const _contexts: Array<{ name: string }> = [
            { name: 'Milliseconds' },
            { name: 'Seconds' },
            { name: 'Minutes' },
            { name: 'Hours' },
            { name: 'Days' },
        ];
        for (const _context of _contexts) {
            context(`method:from${_context.name}`, () => {
                it(`shouldn't throw provided a defined, non-null arg`, () => {
                    for (const _case of _cases.filter(x => !x.isInvalid)) {
                        expect(() => (TimeSpan as any)[`from${_context.name}`](_case.arg as any)).not.to.throw();
                    }
                });
                it(`should throw provided an undefined or null arg`, () => {
                    for (const _case of _cases.filter(x => !!x.isInvalid)) {
                        expect(() => (TimeSpan as any)[`from${_context.name}`](_case.arg as any)).to.throw();
                    }
                });
                it(`should prescribe the "total${_context.name}" property accordingly`, () => {
                    for (const _case of _cases.filter(x => !x.isInvalid)) {
                        expect((TimeSpan as any)[`from${_context.name}`](_case.arg as any)[`total${_context.name}`]).to.equal(_case.arg);
                    }
                });
            });
        }

        context(`the zero property`, () => {
            it(`shouldn't throw`, () => {
                expect(() => TimeSpan.zero).not.to.throw();
            });
            it(`should return the same reference over and over`, () => {
                expect(TimeSpan.zero).to.be.equal(TimeSpan.zero);
            });
            it(`should return a TimeSpan`, () => {
                expect(TimeSpan.zero).to.be.instanceOf(TimeSpan);
            });
            it(`should return a TimeSpan whose totalMilliseconds is 0`, () => {
                expect(TimeSpan.zero.totalMilliseconds).to.equal(0);
            });
        });

        context(`the toTimeSpan method`, () => {
            it(`shouldn't throw for defined and non-null args`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    expect(() => TimeSpan.toTimeSpan(_case.arg as any)).not.to.throw();
                    expect(() => TimeSpan.toTimeSpan(TimeSpan.fromMilliseconds(_case.arg as any))).not.to.throw();
                }
            });
            it(`should throw for undefined or null args`, () => {
                expect(() => TimeSpan.toTimeSpan(null as any)).to.throw();
                expect(() => TimeSpan.toTimeSpan(undefined as any)).to.throw();
            });
            it(`should throw for any arg other than number or TimeSpan`, () => {
                expect(() => TimeSpan.toTimeSpan({} as any))
                    .to.throw(ArgumentOutOfRangeError)
                    .with.property('paramName', 'arg0');
                expect(() => TimeSpan.toTimeSpan(true as any))
                    .to.throw(ArgumentOutOfRangeError)
                    .with.property('paramName', 'arg0');
            });
            it(`should return the exact TimeSpan provided as the single argument`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const arg = TimeSpan.fromMilliseconds(_case.arg as any);
                    expect(TimeSpan.toTimeSpan(arg)).to.equal(arg);
                }
            });
            it(`should return a TimeSpan whose total number of milliseconds as the provided single argument`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    expect(TimeSpan.toTimeSpan(_case.arg as any).totalMilliseconds).to.equal(_case.arg);
                }
            });
        });

        context(`the add method`, () => {
            it(`shouldn't throw provided a truthy arg`, () => {
                const a = TimeSpan.fromMilliseconds(10);
                const b = TimeSpan.fromMilliseconds(20);
                expect(() => a.add(b)).not.to.throw();
            });
            it(`should return a TimeSpan`, () => {
                const a = TimeSpan.fromMilliseconds(10);
                const b = TimeSpan.fromMilliseconds(20);
                expect(a.add(b)).to.be.instanceOf(TimeSpan);
            });
            it(`should return a TimeSpan whose totalMilliseconds is the sum of totalMilliseconds' sum of the two operands`, () => {
                const a = TimeSpan.fromMilliseconds(10);
                const b = TimeSpan.fromMilliseconds(20);
                expect(a.add(b).totalMilliseconds).to.equal(30);
            });
        });

        context(`the subtract method`, () => {
            it(`shouldn't throw provided a truthy arg`, () => {
                const a = TimeSpan.fromMilliseconds(10);
                const b = TimeSpan.fromMilliseconds(20);
                expect(() => a.subtract(b)).not.to.throw();
            });
            it(`should return a TimeSpan`, () => {
                const a = TimeSpan.fromMilliseconds(10);
                const b = TimeSpan.fromMilliseconds(20);
                expect(a.subtract(b)).to.be.instanceOf(TimeSpan);
            });
            it(`should return a TimeSpan whose totalMilliseconds is the totalMilliseconds's difference of the two operands`, () => {
                const a = TimeSpan.fromMilliseconds(10);
                const b = TimeSpan.fromMilliseconds(20);
                expect(a.subtract(b).totalMilliseconds).to.equal(-10);
            });
        });

        context(`the isZero property`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.isZero).not.to.throw();
                }
            });
            it(`should return true if the totalMilliseconds property is 0 and false otherwise`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    const expected = millisecondCount === 0;

                    expect(timeSpan.isZero).to.equal(expected);
                }
            });
        });

        context(`the isNonNegative property`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.isNonNegative).not.to.throw();
                }
            });
            it(`should return true if the totalMilliseconds greater than or equal to 0`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    const expected = millisecondCount >= 0;

                    expect(timeSpan.isNonNegative).to.equal(expected);
                }
            });
        });

        context(`the isNegative property`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.isNegative).not.to.throw();
                }
            });
            it(`should return true if the totalMilliseconds strictly less than 0`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    const expected = millisecondCount < 0;

                    expect(timeSpan.isNegative).to.equal(expected);
                }
            });
        });

        context(`the days property`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.days).not.to.throw();
                }
            });
            it(`should return the number of full days the current TimeSpan represents`, () => {
                expect(TimeSpan.fromHours(48).days).to.equal(2);
                expect(TimeSpan.fromHours(49).days).to.equal(2);
                expect(TimeSpan.fromHours(50).days).to.equal(2);

                expect(TimeSpan.fromMinutes(2880).days).to.equal(2);
                expect(TimeSpan.fromMinutes(2881).days).to.equal(2);
                expect(TimeSpan.fromMinutes(2882).days).to.equal(2);
            });
        });

        context(`the hours property`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.hours).not.to.throw();
                }
            });
            it(`should return the number of full hours days that do not form full days the current TimeSpan represents`, () => {
                expect(TimeSpan.fromDays(1).hours).to.equal(0);
                expect(TimeSpan.fromHours(1).hours).to.equal(1);

                expect(TimeSpan.fromMinutes(120).hours).to.equal(2);
                expect(TimeSpan.fromMinutes(121).hours).to.equal(2);
                expect(TimeSpan.fromMinutes(122).hours).to.equal(2);

                expect(TimeSpan.fromSeconds(7200).hours).to.equal(2);
                expect(TimeSpan.fromSeconds(7201).hours).to.equal(2);
                expect(TimeSpan.fromSeconds(7202).hours).to.equal(2);
            });
        });

        context(`the minutes property`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.minutes).not.to.throw();
                }
            });
            it(`should return the number of full minutes that do not form full hours the current TimeSpan represents`, () => {
                expect(TimeSpan.fromDays(1).minutes).to.equal(0);
                expect(TimeSpan.fromHours(1).minutes).to.equal(0);
                expect(TimeSpan.fromMinutes(1).minutes).to.equal(1);

                expect(TimeSpan.fromSeconds(120).minutes).to.equal(2);
                expect(TimeSpan.fromSeconds(121).minutes).to.equal(2);
                expect(TimeSpan.fromSeconds(122).minutes).to.equal(2);

                expect(TimeSpan.fromMilliseconds(120000).minutes).to.equal(2);
                expect(TimeSpan.fromMilliseconds(120000).minutes).to.equal(2);
                expect(TimeSpan.fromMilliseconds(120000).minutes).to.equal(2);
            });
        });

        context(`the seconds property`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.seconds).not.to.throw();
                }
            });
            it(`should return the number of full minutes that do not form full hours the current TimeSpan represents`, () => {
                expect(TimeSpan.fromDays(1).seconds).to.equal(0);
                expect(TimeSpan.fromHours(1).seconds).to.equal(0);
                expect(TimeSpan.fromMinutes(1).seconds).to.equal(0);
                expect(TimeSpan.fromSeconds(1).seconds).to.equal(1);

                expect(TimeSpan.fromMilliseconds(2000).seconds).to.equal(2);
                expect(TimeSpan.fromMilliseconds(2001).seconds).to.equal(2);
                expect(TimeSpan.fromMilliseconds(2002).seconds).to.equal(2);

                expect(TimeSpan.fromMilliseconds(62000).seconds).to.equal(2);
                expect(TimeSpan.fromMilliseconds(62001).seconds).to.equal(2);
                expect(TimeSpan.fromMilliseconds(62002).seconds).to.equal(2);
            });
        });

        context(`the milliseconds property`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.milliseconds).not.to.throw();
                }
            });
            it(`should return the number of milliseconds that do not form seconds that current TimeSpan represents`, () => {
                expect(TimeSpan.fromDays(1).milliseconds).to.equal(0);
                expect(TimeSpan.fromHours(1).milliseconds).to.equal(0);
                expect(TimeSpan.fromMinutes(1).milliseconds).to.equal(0);
                expect(TimeSpan.fromSeconds(1).milliseconds).to.equal(0);

                expect(TimeSpan.fromMilliseconds(2000).milliseconds).to.equal(0);
                expect(TimeSpan.fromMilliseconds(2001).milliseconds).to.equal(1);
                expect(TimeSpan.fromMilliseconds(2002).milliseconds).to.equal(2);
            });
        });

        context(`the toString method`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => timeSpan.toString()).not.to.throw();
                }
            });

            it(`should return a correct string`, () => {
                expect(TimeSpan.fromDays(Math.PI).toString()).to.equal('3.03:23:53.605');
                expect(TimeSpan.fromDays(Math.E).toString()).to.equal('2.17:14:19.549');
                expect(TimeSpan.fromDays(Math.SQRT2).toString()).to.equal('1.09:56:28.051');
            });
        });

        context(`the toJSON method`, () => {
            it(`shouldn't throw`, () => {
                for (const _case of _cases.filter(x => !x.isInvalid)) {
                    const millisecondCount = _case.arg as any as number;
                    const timeSpan = TimeSpan.fromMilliseconds(millisecondCount);
                    expect(() => JSON.stringify(timeSpan)).not.to.throw();
                }
            });

            it(`should return a correct JSON string`, () => {
                expect(JSON.stringify(TimeSpan.fromDays(Math.PI))).to.equal('"3.03:23:53.605"');
                expect(JSON.stringify(TimeSpan.fromDays(Math.E))).to.equal('"2.17:14:19.549"');
                expect(JSON.stringify(TimeSpan.fromDays(Math.SQRT2))).to.equal('"1.09:56:28.051"');
            });
        });
    });
});
