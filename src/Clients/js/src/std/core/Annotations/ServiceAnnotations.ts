import { PublicCtor } from '../..';

export interface ServiceAnnotations {
    <TService>(target: PublicCtor<TService>): any;
    (args: { endpoint?: string }): any;
}
