import { CoreIpcPlatform } from '../../../../src/foundation/named-pipes/CoreIpcPlatform';

describe(`internals`, () => {
    describe(`CoreIpcPlatform`, () => {
        context(`the getFullPipeName`, () => {
            it(`should prefix "\\\\.\\pipe\\" on Windows`, () => {
                const windows = new CoreIpcPlatform.Windows();
                windows.getFullPipeName('foo')
                    .should.be.eq('\\\\.\\pipe\\foo');
            });

            it(`should prefix "/tmp/CoreFxPipe_" on Linux when the $TMPDIR env variable is not set`, () => {
                withTmpDir('', () => {
                    (new CoreIpcPlatform.DotNetCoreLinux())
                        .getFullPipeName('foo')
                        .should.be.eq('/tmp/CoreFxPipe_foo');
                });
            });

            it(`should prefix "/foo/CoreFxPipe_" on Linux when the $TMPDIR env variable is to "foo"`, () => {
                withTmpDir('/foo/', () => {
                    (new CoreIpcPlatform.DotNetCoreLinux())
                        .getFullPipeName('bar')
                        .should.be.eq('/foo/CoreFxPipe_bar');
                });
            });

            function withTmpDir(value: string, action: () => void): void {
                const old = process.env['TMPDIR'] || '';
                process.env['TMPDIR'] = value;
                try {
                    action();
                } finally {
                    process.env['TMPDIR'] = old;
                }
            }
        });
    });
});
