import { expect } from 'chai';
import { execFileSync } from 'child_process';
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'fs';
import { tmpdir } from 'os';
import { join, resolve } from 'path';
import { webpack } from 'webpack';

const timeout = 120_000;
const packagedBundle = resolve(__dirname, '../../dist/prepack/node/index.js');

const probe = `
const coreipc = require(process.argv[1]);
coreipc.ipc.config.forAnyAddress().setTelemetryPropagation(coreipc.TelemetryPropagationPolicy.Both);
const outgoing = coreipc.TraceContextRuntime.getOutgoingHeaders() ?? null;
const incoming = coreipc.TraceContextRuntime.runIncoming({ traceparent: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01' }, () => 'ran');
process.stdout.write(JSON.stringify({ outgoing, incoming }));
`;

function runProbe(modulePath: string): unknown {
    return JSON.parse(execFileSync(process.execPath, ['-e', probe, modulePath], { encoding: 'utf8' }));
}

describe('node:packaged bundle without @opentelemetry/api', () => {
    let folder: string | undefined;

    beforeAll(() => {
        if (!existsSync(packagedBundle)) {
            return;
        }
        folder = mkdtempSync(join(tmpdir(), 'coreipc-no-otel-'));
        copyFileSync(packagedBundle, join(folder, 'coreipc.js'));
        writeFileSync(join(folder, 'entry.js'), "module.exports = require('./coreipc.js');\n");
    });

    afterAll(() => {
        if (folder) {
            rmSync(folder, { recursive: true, force: true });
        }
    });

    function arrange(): string {
        if (!folder) {
            pending(`Run "npm run build" first: ${packagedBundle} is missing.`);
        }
        expect(() => require.resolve('@opentelemetry/api', { paths: [folder!] })).to.throw();
        return folder!;
    }

    it(
        'should load and run without tracing',
        () => {
            expect(runProbe(join(arrange(), 'coreipc.js'))).to.deep.equal({ outgoing: null, incoming: 'ran' });
        },
        timeout,
    );

    it(
        'should run without tracing when an installed @opentelemetry/api fails to load',
        () => {
            const cwd = arrange();
            const api = join(cwd, 'node_modules', '@opentelemetry', 'api');
            mkdirSync(api, { recursive: true });
            writeFileSync(join(api, 'package.json'), '{ "name": "@opentelemetry/api", "main": "index.js" }\n');
            writeFileSync(join(api, 'index.js'), "throw new Error('broken install');\n");
            try {
                expect(runProbe(join(cwd, 'coreipc.js'))).to.deep.equal({ outgoing: null, incoming: 'ran' });
            } finally {
                rmSync(join(cwd, 'node_modules'), { recursive: true, force: true });
            }
        },
        timeout,
    );

    it(
        "should run without tracing when the installed @opentelemetry/api isn't the API",
        () => {
            const cwd = arrange();
            const api = join(cwd, 'node_modules', '@opentelemetry', 'api');
            mkdirSync(api, { recursive: true });
            writeFileSync(join(api, 'package.json'), '{ "name": "@opentelemetry/api", "main": "index.js" }\n');
            writeFileSync(join(api, 'index.js'), 'module.exports = {};\n');
            try {
                expect(runProbe(join(cwd, 'coreipc.js'))).to.deep.equal({ outgoing: null, incoming: 'ran' });
            } finally {
                rmSync(join(cwd, 'node_modules'), { recursive: true, force: true });
            }
        },
        timeout,
    );

    for (const [name, resolveOptions, warnings] of [
        ['', {}, 1],
        [' that aliases @opentelemetry/api to false', { alias: { '@opentelemetry/api': false } }, 0],
    ] as Array<[string, object, number]>) {
        it(
            `should bundle with a consumer's webpack${name}, and run without tracing`,
            async () => {
                const cwd = arrange();
                const stats = await new Promise<any>((resolvePromise, reject) =>
                    webpack(
                        {
                            mode: 'none',
                            target: 'node',
                            context: cwd,
                            entry: './entry.js',
                            resolve: resolveOptions,
                            output: { path: join(cwd, 'out'), filename: 'bundle.js', library: { type: 'commonjs2' } },
                        },
                        (error, result) => (error ? reject(error) : resolvePromise(result)),
                    ),
                );

                const json = stats.toJson({ all: false, errors: true, warnings: true });
                expect(json.errors).to.deep.equal([]);
                // Without an alias, webpack warns once that it can't resolve the optional peer, as the README says.
                expect(json.warnings.map((warning: { message: string }) => warning.message.includes('@opentelemetry/api'))).to.deep.equal(
                    Array(warnings).fill(true),
                );
                expect(runProbe(join(cwd, 'out', 'bundle.js'))).to.deep.equal({ outgoing: null, incoming: 'ran' });
            },
            timeout,
        );
    }
});

describe('node:packaged bundles where TextDecoder is missing', () => {
    // Jest's jsdom environment has no TextDecoder, so creating one when the module loads would make the import throw.
    const bundles: Array<[string, string]> = [
        ['node', packagedBundle],
        ['web', resolve(__dirname, '../../dist/prepack/web/index.js')],
    ];
    const load = 'delete globalThis.TextDecoder; delete globalThis.TextEncoder; require(process.argv[1]); process.stdout.write("loaded");';

    it(
        'should send a baggage, and take one in, with the node bundle',
        () => {
            if (!existsSync(packagedBundle)) {
                pending(`Run "npm run build" first: ${packagedBundle} is missing.`);
            }
            const run =
                'delete globalThis.TextDecoder; delete globalThis.TextEncoder; const { TraceHeaders } = require(process.argv[1]); ' +
                "process.stdout.write(JSON.stringify([TraceHeaders.fromHeaders({ baggage: 'k=v,x=%C3%A9' }), TraceHeaders.toHeaders({ baggage: 'k=é' })]));";

            expect(JSON.parse(execFileSync(process.execPath, ['-e', run, packagedBundle], { encoding: 'utf8' }))).to.deep.equal([
                { baggage: 'k=v,x=%C3%A9' },
                { baggage: 'k=é' },
            ]);
        },
        timeout,
    );

    for (const [name, bundle] of bundles) {
        it(
            `should load the ${name} bundle`,
            () => {
                if (!existsSync(bundle)) {
                    pending(`Run "npm run build" first: ${bundle} is missing.`);
                }
                expect(execFileSync(process.execPath, ['-e', load, bundle], { encoding: 'utf8' })).to.equal('loaded');
            },
            timeout,
        );
    }
});
