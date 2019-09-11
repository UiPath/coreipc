export class Trace {
    private static readonly _listeners = new Array<(errorOrText: Error | string) => void>();
    public static addListener(listener: (errorOrText: Error | string) => void): void {
        Trace._listeners.push(listener);
    }

    public static log(error: Error): void;
    public static log(text: string): void;
    public static log(errorOrText: Error | string): void {
        for (const listener of Trace._listeners) {
            try {
                listener(errorOrText);
            } catch (_) {
            }
        }
    }
}
