export class IpcError {
    public constructor(
        public readonly Message: string,
        public readonly StackTrace: string,
        public readonly Type: string,
        public readonly InnerError: IpcError | null,
    ) {}
}
