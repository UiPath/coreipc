import { Marshal, SupportedConversion, ArgumentOutOfRangeError } from '..';

/* @internal */
export class BitConverter {
    public static getBytes(value: number, type: 'int32le'): Buffer;
    public static getBytes(value: number, type: 'uint8'): Buffer;

    public static getBytes(value: unknown, type: SupportedConversion): Buffer {
        const buffer = Buffer.alloc(Marshal.sizeOf(type));

        switch (type) {
            case 'int32le':
                buffer.writeInt32LE(value as number, 0);
                break;
            default:
                buffer.writeUInt8(value as number, 0);
                break;
        }

        return buffer;
    }

    public static getNumber(buffer: Buffer, type: 'int32le', offset?: number): number;
    public static getNumber(buffer: Buffer, type: 'uint8', offset?: number): number;

    public static getNumber(buffer: Buffer, type: SupportedConversion): number {
        switch (type) {
            case 'int32le':
                return buffer.readInt32LE(0);
            case 'uint8':
                return buffer.readUInt8(0);

            default:
                throw new ArgumentOutOfRangeError('type');
        }
    }
}
