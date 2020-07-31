import { CoreIpcError } from '.';

export abstract class ArgumentErrorBase extends CoreIpcError {
    protected constructor(
        fallbackMessage: string,
        message?: string,
        public readonly paramName?: string,
    ) {
        super(
            ArgumentErrorBase.computeFullMessage(
                fallbackMessage,
                message,
                paramName));
    }

    protected static computeFullMessage(
        fallbackMessage: string,
        message?: string,
        paramName?: string,
    ): string {
        message = message ?? fallbackMessage;
        return !paramName ? message : `${message} (Parameter: '${paramName}')`;
    }
}
