import { PublicCtor } from '../../src/std';
import { expect } from 'chai';

export function _<T>(sut: () => Promise<T>): _.IAsyncFunc<T> {
    return new AsyncFunc<T>(sut);
}

export module _ {
    export type IAsyncFunc<T> = {
        readonly should: IAsyncFuncShould<T>;
    };

    export type IAsyncFuncShould<T> = {
        throwAsync(): Promise<void>;
        throwAsync<TError>(ctor: PublicCtor<TError>): Promise<void>;
    };
}

class AsyncFunc<T> implements _.IAsyncFunc<T>, _.IAsyncFuncShould<T> {
    constructor(private readonly _sut: () => Promise<T>) {}

    get should(): _.IAsyncFuncShould<T> {
        return this;
    }

    throwAsync(): Promise<void>;
    throwAsync<TError>(ctor: PublicCtor<TError>): Promise<void>;
    async throwAsync<TError>(ctor?: PublicCtor<TError>): Promise<void> {
        let actual = undefined;
        try {
            await this._sut();
        } catch (error) {
            actual = error;
        }

        if (ctor) {
            expect(actual).to.be.instanceOf(ctor);
            return;
        }

        expect(actual).not.to.be.undefined;
    }
}
