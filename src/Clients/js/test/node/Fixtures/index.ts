import { AddressSelectionDelegate, NodeAddressBuilder } from "../../../src/node";

export abstract class AddressHelper {
    public abstract get address(): AddressSelectionDelegate<NodeAddressBuilder>;
    public toString(): string { return `${this.kind}`; }

    protected abstract get kind(): AddressHelper.Kind;
}

export module AddressHelper {
    export enum Kind {
        WebSocket = 'WebSocket',
        NamedPipe = 'NamedPipe',
    }

    module Impl {
        export class WebSocket extends AddressHelper {
            public override get address(): AddressSelectionDelegate<NodeAddressBuilder> { return x => x.isWebSocket('ws://127.0.0.1:61234'); }

            protected override get kind(): AddressHelper.Kind { return Kind.WebSocket; }
        }

        export class NamedPipe extends AddressHelper {
            public override get address(): AddressSelectionDelegate<NodeAddressBuilder> { return x => x.isPipe('uipath-coreipc-test-pipe'); }

            protected override get kind(): AddressHelper.Kind { return Kind.NamedPipe; }
        }
    }

    export const WebSocket = new Impl.WebSocket();
    export const NamedPipe = new Impl.NamedPipe();
}
