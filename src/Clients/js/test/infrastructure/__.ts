const stack = new Array<string>();

export function __for(description: string, specDefinitions: () => void): void {
    stack.push(description);

    const concatenatedDescription = [...stack, description].join(' ');

    describe(concatenatedDescription, specDefinitions);

    stack.pop();
}

export function __fact(
    expectation: string,
    assertion?: jasmine.ImplementationCallback,
    timeout?: number,
): void {
    const concatenatedExpectation = [...stack, expectation].join(' ');

    const newArgs = [...arguments] as Parameters<typeof it>;
    newArgs[0] = concatenatedExpectation;

    it(...newArgs);
}
