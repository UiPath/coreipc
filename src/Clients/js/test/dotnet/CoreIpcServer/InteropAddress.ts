import { Address, assertArgument } from '../../../src/std';
import { NamedPipeAddress } from '../../../src/node';
import { BrowserWebSocketAddress } from '../../../src/web';

export abstract class InteropAddress {
    public static from(address: Address): InteropAddress {
        switch (
            assertArgument({ address }, BrowserWebSocketAddress, NamedPipeAddress)
        ) {
            case BrowserWebSocketAddress: {
                return new InteropAddress.WebSocket(
                    address as BrowserWebSocketAddress
                );
            }
            case NamedPipeAddress: {
                return new InteropAddress.NamedPipe(
                    address as NamedPipeAddress
                );
            }
            default: {
                throw void 0;
            }
        }
    }

    public static computeCommandLineArgs(interopAddresses: InteropAddress[]): string[] { return interopAddresses.flatMap(x => x.commandLineArgs()); }

    public abstract commandLineArgs(): string[];
}

export module InteropAddress {
    export class WebSocket extends InteropAddress {
        constructor(private readonly _underlyingAddress: BrowserWebSocketAddress) { super(); }

        public override commandLineArgs() { return ['--websocket', this._underlyingAddress.url]; }
    }

    export class NamedPipe extends InteropAddress {
        constructor(private readonly _underlyingAddress: NamedPipeAddress) { super(); }

        public override commandLineArgs() { return ['--pipe', this._underlyingAddress.name]; }
    }
}
