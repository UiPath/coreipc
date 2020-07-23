// tslint:disable: no-namespace no-internal-module

import {
    PublicCtor,
    argumentIs,
    TimeSpan,
    ArgumentError,
    ConnectHelper,
} from '../../../foundation';

import {
    ConfigAction,
    ConfigBuilder,
    ConfigNode,
    configNodeDefaults,
} from '.';

import { IIpc } from '../IIpc';

/* @internal */
export class ConfigStoreWrapper {
    public constructor(store?: ConfigStore.IStore) {
        this._store = store ?? new ConfigStore.Store();

        const boundConfig = ConfigStoreWrapper.prototype.config.bind(this);
        const boundRead = ConfigStoreWrapper.prototype.read.bind(this);

        (this as any).config = boundConfig;
        (boundConfig as any).read = boundRead;

        this.iface = boundConfig as any;
    }

    public readonly iface: IIpc.ConfigStore;

    public config(
        action: ConfigAction<ConfigBuilder>): this;

    public config<PipeName extends string = string>(
        pipeName: PipeName,
        action: ConfigAction<ConfigBuilder>): this;

    public config<Service>(
        service: PublicCtor<Service>,
        action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

    public config<Service, PipeName extends string = string>(
        pipeName: PipeName,
        service: PublicCtor<Service>,
        action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

    public config<Service, PipeName extends string = string>(
        service: PublicCtor<Service>,
        pipeName: PipeName,
        action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

    public config<Service, PipeName extends string = string>(
        arg0: PipeName | PublicCtor<Service> | ConfigAction<ConfigBuilder>,
        arg1?: PipeName | PublicCtor<Service> | ConfigAction<ConfigBuilder> | ConfigAction<ConfigBuilder.SetRequestTimeout>,
        arg2?: ConfigAction<ConfigBuilder.SetRequestTimeout>): this {

        argumentIs(arg0, 'arg0', 'string', 'function');
        argumentIs(arg1, 'arg1', 'undefined', 'string', 'function');
        argumentIs(arg2, 'arg2', 'undefined', 'function');

        if (typeof arg0 === 'function' && typeof arg1 === 'undefined' && typeof arg2 === 'undefined') {
            const action = arg0 as ConfigAction<ConfigBuilder>;

            action(this._store.getBuilder());
        } else if (typeof arg0 === 'string' && typeof arg1 === 'function' && typeof arg2 === 'undefined') {
            const pipeName = arg0 as PipeName;
            const action = arg1 as ConfigAction<ConfigBuilder>;

            action(this._store.getBuilder(pipeName));
        } else if (typeof arg0 === 'function' && typeof arg1 === 'function' && typeof arg2 === 'undefined') {
            const service = arg0 as PublicCtor<Service>;
            const action = arg1 as ConfigAction<ConfigBuilder.SetRequestTimeout>;

            action(this._store.getBuilder(service));
        } else if (typeof arg0 === 'string' && typeof arg1 === 'function' && typeof arg2 === 'function') {
            const pipeName = arg0 as PipeName;
            const service = arg1 as PublicCtor<Service>;
            const action = arg2 as ConfigAction<ConfigBuilder.SetRequestTimeout>;

            action(this._store.getBuilder(pipeName, service));
        } else if (typeof arg0 === 'function' && typeof arg1 === 'string' && typeof arg2 === 'function') {
            const service = arg0 as PublicCtor<Service>;
            const pipeName = arg1 as PipeName;
            const action = arg2 as ConfigAction<ConfigBuilder.SetRequestTimeout>;

            action(this._store.getBuilder(pipeName, service));
        } else {
            throw new ArgumentError('Invalid arguments.');
        }

        return this;
    }

    public read<Key extends keyof ConfigNode, PipeName extends string = string, Service = unknown>(
        key: Key,
        pipeName?: PipeName,
        service?: PublicCtor<Service>): ConfigNode[Key] {

        return this._store.readConfig(key, pipeName, service);
    }

    private readonly _store: ConfigStore.IStore;
}

// /* @internal */
// export class ConfigStore implements IIpc.ConfigStore {
//     public constructor(store?: ConfigStore.IStore) {
//         this._store = store ?? new ConfigStore.Store();
//     }

//     public write(
//         action: ConfigAction<ConfigBuilder>): this;

//     public write<PipeName extends string = string>(
//         pipeName: PipeName,
//         action: ConfigAction<ConfigBuilder>): this;

//     public write<Service>(
//         service: PublicCtor<Service>,
//         action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

//     public write<Service, PipeName extends string = string>(
//         pipeName: PipeName,
//         service: PublicCtor<Service>,
//         action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

//     public write<Service, PipeName extends string = string>(
//         service: PublicCtor<Service>,
//         pipeName: PipeName,
//         action: ConfigAction<ConfigBuilder.SetRequestTimeout>): this;

//     public write<Service, PipeName extends string = string>(
//         arg0: PipeName | PublicCtor<Service> | ConfigAction<ConfigBuilder>,
//         arg1?: PipeName | PublicCtor<Service> | ConfigAction<ConfigBuilder> | ConfigAction<ConfigBuilder.SetRequestTimeout>,
//         arg2?: ConfigAction<ConfigBuilder.SetRequestTimeout>): this {

//         argumentIs(arg0, 'arg0', 'string', 'function');
//         argumentIs(arg1, 'arg1', 'undefined', 'string', 'function');
//         argumentIs(arg2, 'arg2', 'undefined', 'function');

//         if (typeof arg0 === 'function' && typeof arg1 === 'undefined' && typeof arg2 === 'undefined') {
//             const action = arg0 as ConfigAction<ConfigBuilder>;

//             action(this._store.getBuilder());
//         } else if (typeof arg0 === 'string' && typeof arg1 === 'function' && typeof arg2 === 'undefined') {
//             const pipeName = arg0 as PipeName;
//             const action = arg1 as ConfigAction<ConfigBuilder>;

//             action(this._store.getBuilder(pipeName));
//         } else if (typeof arg0 === 'function' && typeof arg1 === 'function' && typeof arg2 === 'undefined') {
//             const service = arg0 as PublicCtor<Service>;
//             const action = arg1 as ConfigAction<ConfigBuilder.SetRequestTimeout>;

//             action(this._store.getBuilder(service));
//         } else if (typeof arg0 === 'string' && typeof arg1 === 'function' && typeof arg2 === 'function') {
//             const pipeName = arg0 as PipeName;
//             const service = arg1 as PublicCtor<Service>;
//             const action = arg2 as ConfigAction<ConfigBuilder.SetRequestTimeout>;

//             action(this._store.getBuilder(pipeName, service));
//         } else if (typeof arg0 === 'function' && typeof arg1 === 'string' && typeof arg2 === 'function') {
//             const service = arg0 as PublicCtor<Service>;
//             const pipeName = arg1 as PipeName;
//             const action = arg2 as ConfigAction<ConfigBuilder.SetRequestTimeout>;

//             action(this._store.getBuilder(pipeName, service));
//         } else {
//             throw new ArgumentError('Invalid arguments.');
//         }

//         return this;
//     }

//     public read<Key extends keyof ConfigNode, PipeName extends string = string, Service = unknown>(
//         key: Key,
//         pipeName?: PipeName,
//         service?: PublicCtor<Service>): ConfigNode[Key] {

//         return this._store.readConfig(key, pipeName, service);
//     }

//     private readonly _store: ConfigStore.IStore;
// }

/* @internal */
export module ConfigStore {
    export interface IStore {
        getBuilder(): ConfigBuilder;
        getBuilder(pipeName: string): ConfigBuilder;
        getBuilder(service: PublicCtor): ConfigBuilder.SetRequestTimeout;
        getBuilder(pipeName: string, service: PublicCtor): ConfigBuilder.SetRequestTimeout;
        getBuilder(arg0?: string | PublicCtor, arg1?: PublicCtor): ConfigBuilder | ConfigBuilder.SetRequestTimeout;

        readConfig<
            Key extends keyof ConfigNode,
            PipeName extends string = string,
            Service = unknown>(
                key: Key,
                pipeName?: PipeName,
                service?: PublicCtor<Service>): ConfigNode[Key];
    }

    export class Store {
        private readonly _records = new Array<ConfigRecord>();

        public getBuilder(): ConfigBuilder;
        public getBuilder(pipeName: string): ConfigBuilder;
        public getBuilder(service: PublicCtor): ConfigBuilder.SetRequestTimeout;
        public getBuilder(pipeName: string, service: PublicCtor): ConfigBuilder.SetRequestTimeout;
        public getBuilder(arg0?: string | PublicCtor, arg1?: PublicCtor): ConfigBuilder | ConfigBuilder.SetRequestTimeout {
            argumentIs(arg0, 'arg0', 'undefined', 'string', 'function');
            argumentIs(arg1, 'arg1', 'undefined', 'function');

            const pipeName = typeof arg0 === 'string' ? arg0 : undefined;
            const service = typeof arg0 === 'function' ? arg0 : arg1;

            return new ConfigBuilderImpl(this._records, pipeName, service);
        }

        public readConfig<
            Key extends keyof ConfigNode,
            PipeName extends string = string,
            Service = unknown>(
                key: Key,
                pipeName?: PipeName,
                service?: PublicCtor<Service>): ConfigNode[Key] {

            const result: ConfigNode[Key] =
                this._records.find(makeMatcher(pipeName, service))?.value ??
                this._records.find(makeMatcher(undefined, service))?.value ??
                this._records.find(makeMatcher(pipeName, undefined))?.value ??
                this._records.find(makeMatcher(undefined, undefined))?.value ??
                configNodeDefaults[key];

            return result;

            // tslint:disable-next-line: no-shadowed-variable
            function makeMatcher(pipeName: PipeName | undefined, service: PublicCtor<Service> | undefined): (candidate: ConfigRecord) => boolean {
                return (candidate: ConfigRecord) => candidate.key === key &&
                    candidate.pipeName === pipeName &&
                    candidate.service === service;
            }
        }
    }

    interface ConfigRecord<TKey extends keyof ConfigNode = any> {
        readonly pipeName?: string;
        readonly service?: PublicCtor<unknown>;
        readonly key?: TKey;
        value?: ConfigNode[TKey];
    }

    class ConfigBuilderImpl implements ConfigBuilder {
        public constructor(
            private readonly _records: ConfigRecord[],
            private readonly _pipeName?: string,
            private readonly _service?: PublicCtor<unknown>) { }

        public setRequestTimeout<T>(this: T & ConfigBuilderImpl, value: TimeSpan): Pick<T, Exclude<keyof T, 'setRequestTimeout'>>;
        public setRequestTimeout<T>(this: T & ConfigBuilderImpl, milliseconds: number): Pick<T, Exclude<keyof T, 'setRequestTimeout'>>;
        public setRequestTimeout<T>(this: T & ConfigBuilderImpl, arg0: TimeSpan | number): Pick<T, Exclude<keyof T, 'setRequestTimeout'>> {
            argumentIs(arg0, 'arg0', 'number', TimeSpan);

            this.getOrCreateRecord('requestTimeout').value = TimeSpan.toTimeSpan(arg0);
            return this;
        }

        public allowImpersonation<T>(this: T & ConfigBuilderImpl): Pick<T, Exclude<keyof T, 'allowImpersonation'>> {
            this.getOrCreateRecord('allowImpersonation').value = true;
            return this;
        }

        public setConnectHelper<T>(this: T & ConfigBuilderImpl, connectHelper: ConnectHelper): Pick<T, Exclude<keyof T, 'setConnectHelper'>> {
            argumentIs(connectHelper, 'connectHelper', 'function');

            this.getOrCreateRecord('connectHelper').value = connectHelper;
            return this;
        }

        private getOrCreateRecord<TKey extends keyof ConfigNode>(key: TKey): ConfigRecord<TKey> {
            let result = this._records.find(candidate =>
                candidate.pipeName === this._pipeName &&
                candidate.service === this._service &&
                candidate.key === key);

            if (!result) {
                result = {
                    pipeName: this._pipeName,
                    service: this._service,
                    key,
                };
                this._records.push(result);
            }

            return result;
        }
    }
}
