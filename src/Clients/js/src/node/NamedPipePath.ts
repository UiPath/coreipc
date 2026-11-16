/**
 * Turns the pipe name a caller configures into the path handed to `net.Socket.connect` on macOS.
 *
 * On Windows and Linux the name is mapped the way .NET does it (`\\.\pipe\<name>`, respectively
 * `$TMPDIR/CoreFxPipe_<name>`), see `Platform`. On macOS .NET's mapping does not fit: `$TMPDIR` is
 * `/var/folders/XX/<30 chars>/T/` (49 chars), `CoreFxPipe_` adds 11, and a Unix domain socket path is
 * capped at 104 bytes including the NUL, so any name of about 44+ chars can neither bind nor connect.
 * Hence, on macOS only:
 *   1. a rooted name (starts with `/`) is used verbatim;
 *   2. otherwise the path is `<root>/<name>` with no prefix, where root is `UIPATH_PIPE_ROOT_PATH`
 *      when set and non-empty, else `defaultMacPipeRoot()`;
 *   3. the path is cut to at most 103 UTF-8 bytes without splitting a character.
 *
 * The .NET library and the Python package apply the same rule, so all three compute byte-identical
 * paths. The shared test vectors live in `src/MacPipePathVectors.json`.
 */
/* @internal */
export module NamedPipePath {
    /** Environment variable that overrides the directory macOS pipes are created in. */
    export const rootEnvironmentVariable = 'UIPATH_PIPE_ROOT_PATH';

    /** The longest socket path macOS accepts: `sun_path` is 104 bytes including the terminating NUL. */
    export const maxMacPathBytes = 103;

    /**
     * The directory macOS pipes live in when `UIPATH_PIPE_ROOT_PATH` is not set.
     * For now this is the platform temp directory; a shorter root is being researched separately,
     * and swapping it in should touch only this function.
     */
    export function defaultMacPipeRoot(): string {
        return process.env.TMPDIR || '/tmp/';
    }

    /**
     * The pure form of the macOS rule: environment and default root are injected, so it is
     * unit-tested on any OS.
     * @param pipeName The name the caller configured.
     * @param rootOverride The value of `UIPATH_PIPE_ROOT_PATH`, or `undefined` when unset.
     * @param defaultRoot The directory to use when `rootOverride` is unset or empty.
     */
    export function resolveMac(pipeName: string, rootOverride: string | undefined, defaultRoot: string): string {
        if (pipeName.startsWith('/')) {
            return pipeName;
        }

        const root = rootOverride || defaultRoot;
        const separator = root.endsWith('/') ? '' : '/';
        return truncateUtf8(root + separator + pipeName, maxMacPathBytes);
    }

    /** Cuts `value` to at most `maxBytes` UTF-8 bytes without splitting a character. */
    export function truncateUtf8(value: string, maxBytes: number): string {
        const bytes = Buffer.from(value, 'utf8');
        if (bytes.byteLength <= maxBytes) {
            return value;
        }

        let cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xc0) === 0x80) {
            // step back off continuation bytes to a character boundary
            cut--;
        }
        return bytes.subarray(0, cut).toString('utf8');
    }
}
