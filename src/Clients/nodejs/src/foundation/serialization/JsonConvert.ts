/* @internal */
export class JsonConvert {
    public static deserializeObject<T extends object>(json: string, type: new (...args: any[]) => T): T {
        const result = JSON.parse(json) as T;
        return result.become(type);
    }

    public static serializeObject<T = unknown>(obj: T): string {
        return JSON.stringify(obj);
    }
}
