// tslint:disable: no-namespace no-internal-module

import { TimeSpan, ConnectHelper } from '@foundation';

export type ConfigBuilder =
    ConfigBuilder.SetRequestTimeout &
    ConfigBuilder.AllowImpersonation &
    ConfigBuilder.SetConnectHelper;

export module ConfigBuilder {
    export interface SetRequestTimeout {
        setRequestTimeout<T>(this: T, value: TimeSpan): Omit<T, keyof SetRequestTimeout>;
        setRequestTimeout<T>(this: T, milliseconds: number): Omit<T, keyof SetRequestTimeout>;
    }

    export interface AllowImpersonation {
        allowImpersonation<T>(this: T): Omit<T, keyof AllowImpersonation>;
    }

    export interface SetConnectHelper {
        setConnectHelper<T>(this: T, connectHelper: ConnectHelper): Omit<T, keyof SetConnectHelper>;
    }
}
