import { DotNetScript, DotNetScriptEvent } from '.';

async function main(): Promise<void> {
    console.log('Starting 1st...');
    await first();

    console.log('Starting 2nd...');
    await second();
}
async function first(): Promise<void> {
    console.log();
    console.log();
    console.log('Running 1st method....');
    console.log('=========================');
    console.log();
    console.log();

    let dns: DotNetScript | null = null;
    try {
        dns = await DotNetScript.startAsync(
            // javascript
            `#! "netcoreapp2.0"

Console.WriteLine("Up and running...");
var line = Console.ReadLine();
Console.WriteLine($"You said: \\"{line}\\"");
        `,
            'E:\\Ipc\\clients\\nodejs\\$tools\\dotnet-script\\dotnet-script.cmd',
            'E:\\Ipc\\clients\\nodejs\\$uipath-ipc-dotnet'
        );

        await dns.waitAsync(x => x.type === 'line' && x.line === 'Up and running...');
        dns.sendLineAsync('Hello .NET!');
        const line = await dns.waitAsync(x => x.type === 'line');

        console.log(`Received: ${JSON.stringify(line)}`);
    } finally {
        if (dns) {
            await dns.disposeAsync();
        }
    }
}

async function second(): Promise<void> {
    console.log();
    console.log();
    console.log('Running 2nd method....');
    console.log('=========================');
    console.log();
    console.log();
    let dns: DotNetScript | null = null;
    try {
        dns = await DotNetScript.startAsync(
            // javascript
            `#! "netcoreapp2.0"

Console.WriteLine("(2nd) Up and running...");
var line = Console.ReadLine();
Console.WriteLine($"(2nd) You said: \\"{line}\\"");
        `,
            'E:\\Ipc\\clients\\nodejs\\$tools\\dotnet-script\\dotnet-script.cmd',
            'E:\\Ipc\\clients\\nodejs\\$uipath-ipc-dotnet'
        );

        await dns.waitAsync(x => x.type === 'line' && x.line === '(2nd) Up and running...');
        dns.sendLineAsync('(2nd) Hello .NET!');
        const line = await dns.waitAsync(x => x.type === 'line');

        console.log(`(2nd) Received: ${JSON.stringify(line)}`);
    } finally {
        if (dns) {
            await dns.disposeAsync();
        }
    }
}

main().then(
    _ => { },
    err => { console.error(err); }
);
