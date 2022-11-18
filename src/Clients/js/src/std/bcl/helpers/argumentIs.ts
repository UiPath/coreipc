import { ExtendedType } from '../reflection';
import { ArgumentNullError, ArgumentOutOfRangeError, ArgumentError } from '..';
import { nameof } from '.';

function describeExtendedType(type: ExtendedType): string {
    argumentIs(type, 'type', 'string', Function);

    if (typeof type === 'string') {
        return `'${type}'`;
    } else {
        return type.name;
    }
}

function argumentIs<T>(
    arg: T,
    paramName: string,
    ...extendedTypes: ExtendedType[]
): ExtendedType | never {
    if (paramName == null) {
        throw new ArgumentNullError('paramName');
    }
    if (paramName === '') {
        throw new ArgumentOutOfRangeError('paramName', 'Specified paramName was an empty string.');
    }

    if (-1 === extendedTypes.indexOf('undefined') && arg == null) {
        throw new ArgumentNullError(paramName);
    }
    if (extendedTypes == null) {
        throw new ArgumentNullError(paramName);
    }
    if (extendedTypes.length === 0) {
        throw new ArgumentError(`Specified argument contained zero elements.`, 'extendedTypes');
    }

    const invalidExtendedTypes = extendedTypes.filter((extendedType) => {
        return (
            typeof extendedType !== 'function' &&
            (typeof extendedType !== 'string' ||
                (extendedType !== 'object' &&
                    extendedType !== 'string' &&
                    extendedType !== 'undefined' &&
                    extendedType !== 'number' &&
                    extendedType !== 'bigint' &&
                    extendedType !== 'boolean' &&
                    extendedType !== 'function' &&
                    extendedType !== 'symbol'))
        );
    });

    if (invalidExtendedTypes.length > 0) {
        const strInvalidExtendedTypes = invalidExtendedTypes.map(describeExtendedType).join(', ');
        throw new ArgumentError(
            `Specified argument contained invalid elements: [${strInvalidExtendedTypes}]`,
            'extendedTypes',
        );
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
            throw new ArgumentOutOfRangeError(
                paramName,
                `Specified argument was not of type '${extendedTypes[0]}'.`,
            );
        } else if (typeof arg !== 'object') {
            throw new ArgumentOutOfRangeError(
                paramName,
                `Specified argument was not of type 'object'.`,
            );
        } else {
            throw new ArgumentOutOfRangeError(
                paramName,
                `Specified argument was an 'object' but not an instance of ${extendedTypes[0].name}.'`,
            );
        }
    } else {
        throw new ArgumentOutOfRangeError(
            paramName,
            `Specified argument's type was neither of: ${extendedTypes
                .map(describeExtendedType)
                .join(', ')}.`,
        );
    }
}

/* @internal */
export function assertArgument<T>(
    argWrapper: { [key: string]: T },
    ...extendedTypes: ExtendedType[]
): ExtendedType | never {
    const paramName = nameof(argWrapper);
    return argumentIs(argWrapper[paramName], paramName, ...extendedTypes);
}
