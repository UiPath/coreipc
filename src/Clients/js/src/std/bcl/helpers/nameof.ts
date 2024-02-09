export function nameof(wrapper: { [key: string]: any }): string {
    return Object.keys(wrapper)[0];
}
