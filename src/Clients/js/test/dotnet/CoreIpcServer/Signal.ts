export interface Signal<TDetails extends Signal.ExceptionDetails = Signal.ExceptionDetails> {
    Kind: Signal.Kind;
    Details: TDetails;
}

export module Signal {
    export enum Kind {
        Throw = 'Throw',
        PoweringOn = 'PoweringOn',
        ReadyToConnect = 'ReadyToConnect',
    }
    export interface ExceptionDetails {
        Type: string;
        Message: string;
    }
}
