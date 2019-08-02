export type Func0<TResult> = () => TResult;
export type Func1<T1, TResult> = (item1: T1) => TResult;
export type Func2<T1, T2, TResult> = (item1: T1, item2: T2) => TResult;

export type Action0 = Func0<void>;
export type Action1<T1> = Func1<T1, void>;
export type Action2<T1, T2> = Func2<T1, T2, void>;
