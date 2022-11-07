export function constructing<TConstructor extends new (...args: any[]) => any>(
    ctor: TConstructor, ...args: ConstructorParameters<TConstructor>): () => InstanceType<TConstructor> {
    return () => new ctor(...args);
}

class ConstructorArgListImpl<TConstructor extends new (...args: any[]) => InstanceType<TConstructor>>
    implements ConstructorArgList<TConstructor> {

    private readonly _list = new Array<ConstructorParameters<TConstructor>>();

    withArgs(...args: ConstructorParameters<TConstructor>): this {
        this._list.push(args);
        return this;
    }
    forEach(test: (args: ConstructorParameters<TConstructor>) => void): void {
        for (const args of this._list) {
            test(args);
        }
    }
}

export function ctor<TConstructor extends new (...args: any[]) => InstanceType<TConstructor>>
    (ctor: TConstructor) : ConstructorArgList<TConstructor> {
    return new ConstructorArgListImpl();
}

export interface ConstructorArgList<TConstructor extends new (...args: any[]) => InstanceType<TConstructor>> {
    withArgs(...args: ConstructorParameters<TConstructor>): this;
    forEach(test: (args: ConstructorParameters<TConstructor>) => void): void;
}

class ConstructingTheoryImpl<TConstructor extends new (...args: any[]) => InstanceType<TConstructor>>
    implements ConstructingTheory<TConstructor> {

    private readonly _cases = new Array<ConstructorParameters<TConstructor>>();

    constructor(public readonly ctor: TConstructor) { }

    shouldNotThrow(): void {
        for (const args of this._cases) {
            test(`${this.ctor.name}(${args}) should not throw`, () => {
                const act = constructing(this.ctor, ...args);
                expect(act).not.toThrow();
            });
        }
    }

    withArgs(...args: ConstructorParameters<TConstructor>): ConstructingTheory<TConstructor> {
        this._cases.push(args);
        return this;
    }
}

export function constructing2<TConstructor extends new (...args: any[]) => InstanceType<TConstructor>>
    (ctor: TConstructor)
    : ConstructingTheory<TConstructor> {
    return new ConstructingTheoryImpl(ctor);
}

export interface ConstructingTheory<TConstructor extends new (...args: any[]) => InstanceType<TConstructor>> {
    readonly ctor: TConstructor;

    withArgs(...args: ConstructorParameters<TConstructor>): ConstructingTheory<TConstructor>;

    shouldNotThrow(): void;
}

export * from 'ts-simple-nameof';
