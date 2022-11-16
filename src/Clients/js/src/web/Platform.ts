import {
    InvalidOperationError,
    PlatformNotSupportedError,
    IPlatform,
} from '../std';

/* @internal */
export module Platform {
    export const current = create();

    function create(): IPlatform<Id> {
        switch (detect()) {
            case Id.Web:
                return new Web();
            default:
                throw new InvalidOperationError();
        }
    }

    function detect(): Id {
        return Id.Web;
    }

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
}
