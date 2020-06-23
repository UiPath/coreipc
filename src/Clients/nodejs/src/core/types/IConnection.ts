export interface IConnection {
    write(buffer: Buffer): Promise<void>;
}
