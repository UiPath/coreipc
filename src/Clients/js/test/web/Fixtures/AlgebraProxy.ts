import { proxyFactory } from './ProxyFactory';
import { IAlgebra } from '../Contracts';

export const algebraProxyFactory = () => proxyFactory.withService(IAlgebra);
