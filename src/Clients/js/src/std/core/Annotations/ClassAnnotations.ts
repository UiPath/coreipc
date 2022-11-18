import { PublicCtor } from '../..';

export interface ClassAnnotations {
    <TService>(target: PublicCtor<TService>): any;
    (args: { endpoint?: string }): any;
}
