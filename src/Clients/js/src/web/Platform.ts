import {
    InvalidOperationError,
    PlatformNotSupportedError,
    IPlatform,
} from '../std';

/* @internal */
export module Platform {
    export enum Id {
        Web,
    }

    class Web implements IPlatform<Id> {
        get id(): Id {
            return Id.Web;
        }

        getFullPipeName(shortName: string): string {
            throw new PlatformNotSupportedError();
        }
    }

    function detect(): Id {
        return Id.Web;
    }

    function create(): IPlatform<Id> {
        switch (detect()) {
            case Id.Web:
                return new Web();
            default:
                throw new InvalidOperationError();
        }
    }

    export const current = create();
}
