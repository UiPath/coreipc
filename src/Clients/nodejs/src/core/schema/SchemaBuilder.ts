import { MethodNameEnumerator, PublicCtor, argumentIs } from '@foundation';
import { rtti } from '@core-rtti';
import { Schema, MethodSchemaBuilder } from '.';
import { MethodSchema } from './schema-types';

/* @internal */
export class SchemaBuilder<T = unknown> {
    public static build<TContract>(contract: PublicCtor<TContract>): Schema {
        argumentIs(contract, 'contract', 'function');

        const builder = new SchemaBuilder(contract);
        builder.run();
        return builder._output;
    }

    private _output: Schema = null as any;

    private constructor(contract: PublicCtor<T>);
    private constructor(private readonly _contract: PublicCtor<T>) { }

    private run(): void {
        const classInfo = rtti.ClassInfo.get(this._contract);
        const boundMethodSchemaBuilder = MethodSchemaBuilder.build(this._contract);

        function createPair(methodName: string & keyof T): { readonly [methodName: string]: MethodSchema } {
            return { [methodName]: boundMethodSchemaBuilder(methodName) };
        }
        const pairs = MethodNameEnumerator.enumerate(this._contract).map(createPair);

        let methods: { readonly [methodName: string]: MethodSchema } = {};
        for (const pair of pairs) {
            methods = { ...methods, ...pair };
        }

        this._output = {
            endpointName: classInfo.endpointNameOverride ?? classInfo.ctor.name,
            className: classInfo.ctor.name,
            methods,
        };
    }
}
