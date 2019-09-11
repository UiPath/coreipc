import * as blessed from 'blessed';
import { PromiseCompletionSource } from '@uipath/ipc';
import { IDisposable } from '@uipath/ipc/dist/foundation/disposable/disposable';

export interface ITerminalColorSettings {
    cursor: string;
    border: string;
    accent: string;
}
export interface ITerminalSettings {
    title: string;
    colors: ITerminalColorSettings;
}

export class Terminal implements IDisposable {
    private static createDefaultSettings(): ITerminalSettings {
        return {
            title: 'My Terminal',
            colors: {
                cursor: '#FFB40E',
                border: '#FA4616',
                accent: '#FFB40E'
            }
        };
    }

    private readonly _settings: ITerminalSettings;
    private readonly _screen: blessed.Widgets.Screen;
    private readonly _form: blessed.Widgets.FormElement<void>;
    private readonly _log1: blessed.Widgets.BoxElement;
    private readonly _log2: blessed.Widgets.BoxElement;
    private readonly _command: blessed.Widgets.TextboxElement;

    private _maybeReadLinePcs: PromiseCompletionSource<void> | null = null;

    constructor(config?: (settings: ITerminalSettings) => void) {
        this._settings = Terminal.createDefaultSettings();
        if (config) { config(this._settings); }

        this._screen = this.createScreen();
        this._form = this.createForm();
        this._log1 = this.createLog1();
        this._log2 = this.createLog2();
        this._command = this.createCommand();

        this._screen.on('resize', () => {
            this._log1.setScrollPerc(100);
            this._screen.render();
        });

        this._screen.render();
    }

    private createScreen(): blessed.Widgets.Screen {
        const screen = blessed.screen({
            smartCSR: true,
            terminal: 'xterm-256color',
            cursor: {
                artificial: true,
                blink: true,
                color: this._settings.colors.cursor,
                shape: 'block'
            },
            title: this._settings.title
        });
        return screen;
    }
    private createForm(): blessed.Widgets.FormElement<void> {
        const form = blessed.form({
            parent: this._screen,
            top: 'center',
            left: 'center',
            width: '100%',
            height: '100%',
            content: ` ${this._settings.title}`,
            tags: true,
            border: 'line',
            style: {
                fg: 'white',
                bg: 'black',
                border: {
                    fg: this._settings.colors.border,
                    bg: 'black'
                }
            }
        });
        const line = blessed.line({
            parent: form,
            left: 0,
            right: 0,
            top: 1,
            orientation: 'horizontal',
            fg: this._settings.colors.border,
            bg: 'black'
        });

        blessed.line({
            parent: form,
            top: 2,
            right: 71,
            bottom: 1,
            orientation: 'vertical',
            fg: this._settings.colors.border,
            bg: 'black'
        });

        blessed.line({
            parent: form,
            left: 0,
            right: 0,
            bottom: 1,
            orientation: 'horizontal',
            fg: this._settings.colors.border,
            bg: 'black'
        });

        return form as any;
    }
    private createLog1(): blessed.Widgets.BoxElement {
        return blessed.scrollabletext({
            parent: this._form,
            left: 1,
            top: 2,
            right: 72,
            bottom: 2,
            tags: true,
            bg: 'black',
            fg: 'white',
            focusable: false
        });
    }
    private createLog2(): blessed.Widgets.BoxElement {
        return blessed.scrollabletext({
            parent: this._form,
            width: 70,
            top: 2,
            right: 1,
            bottom: 2,
            tags: true,
            bg: 'black',
            fg: 'white',
            focusable: false
        });
    }
    private createCommand(): blessed.Widgets.TextboxElement {
        const command = blessed.textbox({
            parent: this._form,
            left: 3,
            right: 0,
            bottom: 0,
            height: 1,
            bg: 'black',
            fg: this._settings.colors.accent,
            keys: true,
            mouse: true,
            inputOnFocus: true
        });
        const textCharon = blessed.text({
            parent: this._form,
            left: 1,
            bottom: 0,
            // top: 0,
            width: 1,
            height: 1,
            fg: this._settings.colors.accent,
            bg: 'black',
            content: '>'
        });

        command.on('submit', () => {
            if (this._maybeReadLinePcs) {
                this._maybeReadLinePcs.setResult(undefined);
                this._maybeReadLinePcs = null;
            }
        });
        return command;
    }

    public get annex(): string { return this._log2.getContent(); }
    public set annex(value: string) {
        this._log2.setContent(value);
        this._screen.render();
    }

    public write(text: string): void {
        this._log1.setContent(`${this._log1.getContent()}${text}`);
        this._log1.setScrollPerc(100);
        this._screen.render();
    }
    public writeLine(text?: string): void {
        text = text || '';
        this._log1.setContent(`${this._log1.getContent()}${text}\r\n`);
        this._log1.setScrollPerc(100);
        this._screen.render();
    }
    public initialize(text: string): void { this.writeLine(`${'\r\n'.repeat(80)}${text}`); }
    public async readLine(postprocess?: (line: string) => string): Promise<string> {
        if (this._maybeReadLinePcs) {
            throw new Error(`You can't readLine concurrently`);
        }

        this._command.focus();
        this._maybeReadLinePcs = new PromiseCompletionSource<void>();
        await this._maybeReadLinePcs.promise;

        let line = this._command.getValue();
        if (postprocess) { line = postprocess(line); }
        this.writeLine(`{yellow-fg}> ${line}{/yellow-fg}`);
        this._command.clearValue();
        return line;
    }

    public dispose(): void {
        this._screen.destroy();
    }
}
