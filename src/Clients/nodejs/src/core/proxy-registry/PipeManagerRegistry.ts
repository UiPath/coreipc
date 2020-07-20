import { PipeManager } from '.';
import { IIpcInternal } from '../ipc';

/* @internal */
export class PipeManagerRegistry {
    constructor(
        private readonly _owner: IIpcInternal,
    ) { }

    public get(pipeName: string): PipeManager {
        return this._map.get(pipeName) ?? this.add(pipeName);
    }

    private add(pipeName: string): PipeManager {
        const pipeManager = new PipeManager(this._owner, pipeName);
        this._map.set(pipeName, pipeManager);
        return pipeManager;
    }

    private readonly _map = new Map<string, PipeManager>();
}
