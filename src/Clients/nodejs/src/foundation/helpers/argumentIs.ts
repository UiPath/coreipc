/* istanbul ignore file */

import { ArgumentNullError, ArgumentOutOfRangeError } from '../../foundation';
import { ArgumentError } from '../errors/ArgumentError';

/* @internal */
export type PrimitiveNonObjectType = 'string' | 'number' | 'bigint' | 'boolean' | 'symbol' | 'function';

/* @internal */
export type PrimitiveDefinedType = PrimitiveNonObjectType | 'object';

/* @internal */
export type PrimitiveType = PrimitiveDefinedType | 'undefined';

/* @internal */
// tslint:disable-next-line: ban-types
export type ExtendedType = PrimitiveType | Function;

/* @internal */
export function argumentIs<T>(arg: T, paramName: string, ...extendedTypes: ExtendedType[]): ExtendedType | never {
    if (paramName == null) { throw new ArgumentNullError('paramName'); }
    if (paramName === '') { throw new ArgumentOutOfRangeError('paramName', 'Specified paramName was an empty string.'); }

    if (-1 === extendedTypes.indexOf('undefined') && arg == null) { throw new ArgumentNullError(paramName); }
    if (extendedTypes == null) { throw new ArgumentNullError(paramName); }
    if (extendedTypes.length === 0) { throw new ArgumentError(`Specified argument contained zero elements.`, 'extendedTypes'); }

    const invalidExtendedTypes = extendedTypes.filter(extendedType => {
        return typeof extendedType !== 'function' &&
            (typeof extendedType !== 'string' || (
                extendedType !== 'object' &&
                extendedType !== 'string' &&
                extendedType !== 'undefined' &&
                extendedType !== 'number' &&
                extendedType !== 'bigint' &&
                extendedType !== 'boolean' &&
                extendedType !== 'function' &&
                extendedType !== 'symbol'
            ));
    });

    if (invalidExtendedTypes.length > 0) {
        const strInvalidExtendedTypes = invalidExtendedTypes.map(describeExtendedType).join(', ');
        throw new ArgumentError(`Specified argument contained invalid elements: [${strInvalidExtendedTypes}]`, 'extendedTypes');
    }

    for (const extendedType of extendedTypes) {
        if (typeof extendedType === 'function') {
            if (arg instanceof extendedType) {
                return extendedType;
            }
        } else {
            if (typeof arg === extendedType) {
                return extendedType;
            }
        }
    }

    if (extendedTypes.length === 1) {
        if (typeof extendedTypes[0] === 'string') {
            throw new ArgumentOutOfRangeError(paramName, `Specified argument was not of type '${extendedTypes[0]}'.`);
        } else if (typeof arg !== 'object') {
            throw new ArgumentOutOfRangeError(paramName, `Specified argument was not of type 'object'.`);
        } else {
            throw new ArgumentOutOfRangeError(paramName, `Specified argument was an 'object' but not an instance of ${extendedTypes[0].name}.'`);
        }
    } else {
        throw new ArgumentOutOfRangeError(paramName, `Specified argument's type was neither of: ${extendedTypes.map(describeExtendedType).join(', ')}.`);
    }
}

/* @internal */
export function argumentIsNonEmptyString(arg: any, paramName: string, message?: string): arg is string | never {
    argumentIs(arg, paramName, 'string');

    if (arg as any === '') {
        throw new ArgumentOutOfRangeError(paramName, message ?? 'Specified argument was an empty string.');
    }

    return true;
}

function describeExtendedType(type: ExtendedType): string {
    argumentIs(type, 'type', 'string', Function);

    if (typeof type === 'string') {
        return `'${type}'`;
    } else {
        return type.name;
    }
}
