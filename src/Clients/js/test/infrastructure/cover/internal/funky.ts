export type MethodNames<T> = keyof Where<T, string, (...args: any[]) => any>;

export type MethodArgs<T, K extends MethodNames<T>> = T[K] extends (...args: any[]) => any
    ? OverloadedParameters<T[K]>
    : never;

export type MethodArgsSimple<T, K extends MethodNames<T>> = T[K] extends (
    ...args: any[]
) => any
    ? Parameters<T[K]>
    : never;

export type Where<
    Source,
    KeyCondition extends string | number | symbol,
    ValueCondition,
> = Pick<
    Source,
    {
        [K in KeyCondition & keyof Source]: Source[K] extends ValueCondition ? K : never;
    }[KeyCondition & keyof Source]
>;

export type Overloads<T extends (...args: any[]) => any> = T extends {
    (...args: infer A1): infer R1;
    (...args: infer A2): infer R2;
    (...args: infer A3): infer R3;
    (...args: infer A4): infer R4;
    (...args: infer A5): infer R5;
    (...args: infer A6): infer R6;
}
    ?
          | ((...args: A1) => R1)
          | ((...args: A2) => R2)
          | ((...args: A3) => R3)
          | ((...args: A4) => R4)
          | ((...args: A5) => R5)
          | ((...args: A6) => R6)
    : T extends {
          (...args: infer A1): infer R1;
          (...args: infer A2): infer R2;
          (...args: infer A3): infer R3;
          (...args: infer A4): infer R4;
          (...args: infer A5): infer R5;
      }
    ?
          | ((...args: A1) => R1)
          | ((...args: A2) => R2)
          | ((...args: A3) => R3)
          | ((...args: A4) => R4)
          | ((...args: A5) => R5)
    : T extends {
          (...args: infer A1): infer R1;
          (...args: infer A2): infer R2;
          (...args: infer A3): infer R3;
          (...args: infer A4): infer R4;
      }
    ?
          | ((...args: A1) => R1)
          | ((...args: A2) => R2)
          | ((...args: A3) => R3)
          | ((...args: A4) => R4)
    : T extends {
          (...args: infer A1): infer R1;
          (...args: infer A2): infer R2;
          (...args: infer A3): infer R3;
      }
    ? ((...args: A1) => R1) | ((...args: A2) => R2) | ((...args: A3) => R3)
    : T extends { (...args: infer A1): infer R1; (...args: infer A2): infer R2 }
    ? ((...args: A1) => R1) | ((...args: A2) => R2)
    : T extends { (...args: infer A1): infer R1 }
    ? (...args: A1) => R1
    : never;

export type OverloadedParameters<T extends (...args: any[]) => any> = Parameters<
    Overloads<T>
>;
