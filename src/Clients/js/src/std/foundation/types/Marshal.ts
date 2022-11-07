import {
    SupportedConversion,
    ArgumentOutOfRangeError,
} from '@foundation';

/* @internal */
export class Marshal {
    public static sizeOf(type: SupportedConversion): number {
        switch (type) {
            case 'int32le': return 4;
            case 'uint8': return 1;

            default:
                throw new ArgumentOutOfRangeError('type');
        }
    }
}
