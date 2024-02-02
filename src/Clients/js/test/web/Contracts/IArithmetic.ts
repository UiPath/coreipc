import { Message } from '../../../src/std';


export interface IArithmetic {
    Sum(x: number, y: number): Promise<number>;
    SendMessage(message: Message<number>): Promise<boolean>;
}
