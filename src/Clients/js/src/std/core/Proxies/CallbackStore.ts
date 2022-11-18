import { Address } from "../Addresses";

/* @internal */
export class CallbackStore {
    get<TAddress extends Address>(Endpoint: string, _address: TAddress): any {
        throw new Error('Method not implemented.');
    }

}
