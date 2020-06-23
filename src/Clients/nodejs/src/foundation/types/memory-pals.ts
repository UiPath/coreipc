/* @internal */
export class Marshal {
    public static sizeOf(type: SupportedConversion): number {
        switch (type) {
            case 'int32be':
            case 'int32le':
            case 'uint32be':
            case 'uint32le': return 4;

            case 'int8':
            case 'uint8': return 1;

            default:
                throw new Error('Not supported.');
        }
    }
}

/* @internal */
export class BitConverter {
    public static getBytes(value: number, type: 'int32le', offset?: number): Buffer;
    public static getBytes(value: number, type: 'int32be', offset?: number): Buffer;
    public static getBytes(value: number, type: 'uint32le', offset?: number): Buffer;
    public static getBytes(value: number, type: 'uint32be', offset?: number): Buffer;
    public static getBytes(value: number, type: 'uint8', offset?: number): Buffer;
    public static getBytes(value: number, type: 'int8', offset?: number): Buffer;

    public static getBytes(value: unknown, type: SupportedConversion, offset: number = 0): Buffer {
        const buffer = Buffer.alloc(Marshal.sizeOf(type));

        switch (type) {
            case 'int32le': buffer.writeInt32LE(value as number, offset); break;
            case 'int32be': buffer.writeInt32BE(value as number, offset); break;

            case 'uint32le': buffer.writeUInt32LE(value as number, offset); break;
            case 'uint32be': buffer.writeUInt32BE(value as number, offset); break;

            case 'uint8': buffer.writeUInt8(value as number, offset); break;
            case 'int8': buffer.writeInt8(value as number, offset); break;

            default:
                throw new Error('Not supported.');
        }

        return buffer;
    }

    public static getNumber(buffer: Buffer, type: 'int32le', offset?: number): number;
    public static getNumber(buffer: Buffer, type: 'int32be', offset?: number): number;
    public static getNumber(buffer: Buffer, type: 'uint32le', offset?: number): number;
    public static getNumber(buffer: Buffer, type: 'uint32be', offset?: number): number;
    public static getNumber(buffer: Buffer, type: 'uint8', offset?: number): number;
    public static getNumber(buffer: Buffer, type: 'int8', offset?: number): number;

    public static getNumber(buffer: Buffer, type: SupportedConversion, offset: number = 0): number {
        switch (type) {
            case 'int32le': return buffer.readInt32LE(offset);
            case 'int32be': return buffer.readInt32BE(offset);

            case 'uint32le': return buffer.readUInt32LE(offset);
            case 'uint32be': return buffer.readUInt32BE(offset);

            case 'uint8': return buffer.readUInt8(offset);
            case 'int8': return buffer.readInt8(offset);

            default:
                throw new Error('Not supported.');
        }
    }
}

/* @internal */
export type SupportedConversion =
    'int32le' |
    'int32be' |
    'uint32le' |
    'uint32be' |
    'uint8' |
    'int8'
    ;
