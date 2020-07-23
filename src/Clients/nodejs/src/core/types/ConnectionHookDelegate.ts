import { IConnection, ConnectionFactoryDelegate } from '.';
import { CancellationToken } from '../../foundation';

export type ConnectionHookDelegate = (defaultFactory: ConnectionFactoryDelegate, ct: CancellationToken) => Promise<IConnection | void>;
