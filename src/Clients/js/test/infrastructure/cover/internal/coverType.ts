import { PublicCtor } from '../../../../src/std';
import { CoverTypeContext, CoverTypeContextFactory } from '.';

export function cover<TStatic extends abstract new (...args: any) => any>(
    type: PublicCtor<InstanceType<TStatic>>,
    spec: (this: CoverTypeContext<InstanceType<TStatic>, TStatic>) => void,
): void {
    const context = CoverTypeContextFactory.create<InstanceType<TStatic>, TStatic>(type);

    describe(`🌲 "${type.name}"`, () => {
        spec.call(context);
    });
}

export function fcover<TStatic extends abstract new (...args: any) => any>(
    type: PublicCtor<InstanceType<TStatic>>,
    spec: (this: CoverTypeContext<InstanceType<TStatic>, TStatic>) => void,
): void {
    const context = CoverTypeContextFactory.create<InstanceType<TStatic>, TStatic>(type);

    fdescribe(`🌲 "${type.name}"`, () => {
        spec.call(context);
    });
}
