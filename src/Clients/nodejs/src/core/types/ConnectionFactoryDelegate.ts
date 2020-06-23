import { IConnection } from '.';

export type ConnectionFactoryDelegate = () => Promise<IConnection>;
