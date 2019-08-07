export class RethrownError extends Error {
    constructor(public readonly innerError: Error, message: string | undefined) {
        super(message || innerError.message);
    }

    public toString(): string {
        const indentedInnerToString = RethrownError.indent(this.innerError.toString(), 'innerError: ');
        return `${super.toString()}\r\n${indentedInnerToString}`;
    }

    private static indent(input: string, header: string): string {
        const spaces = ' '.repeat(header.length);

        return input
            .replace('\r\n', '\n')
            .split('\n')
            .map((line, index) => `${(0 === index) ? header : spaces}${line}`)
            .reduce((sum, x) => `${sum}\r\n${x}`, '');
    }
}
