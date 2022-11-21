export function __members<T extends Function>(type: T): Members<T> {
    const staticMethods = Object.getOwnPropertyNames(type).map(x => ({
        [x]: `📞 "${x}" static method`,
    }));
    const instanceMethods = Object.getOwnPropertyNames(type.prototype).map(x => ({
        [x]: `📞 "${x}" instance method`,
    }));

    let result = {};

    for (const obj of staticMethods) {
        result = { ...result, ...obj };
    }
    for (const obj of instanceMethods) {
        result = { ...result, ...obj };
    }

    return result as any;
}

export type Members<T extends Function> = Members.Instance<T> & Members.Static<T>;

export module Members {
    export type Instance<T extends Function> = {
        readonly [name in keyof T['prototype']]: keyof T['prototype'];
    };

    export type Static<T extends Function> = {
        readonly [name in keyof T]: keyof T;
    };
}
