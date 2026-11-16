using System.Runtime.InteropServices;
using System.Text;

namespace UiPath.Ipc.Transport.NamedPipe;

/// <summary>
/// Turns the <c>PipeName</c> a caller configures into the name handed to
/// <see cref="System.IO.Pipes.NamedPipeServerStream"/> and <see cref="System.IO.Pipes.NamedPipeClientStream"/>.
/// Every place in this library that opens a pipe by name goes through <see cref="Resolve(string)"/>.
/// <para>
/// On Windows and Linux the name is passed through untouched and .NET does its own mapping
/// (<c>\\.\pipe\&lt;name&gt;</c>, respectively <c>$TMPDIR/CoreFxPipe_&lt;name&gt;</c>).
/// </para>
/// <para>
/// On macOS .NET's mapping does not fit: <c>$TMPDIR</c> is <c>/var/folders/XX/&lt;30 chars&gt;/T/</c> (49 chars),
/// <c>CoreFxPipe_</c> adds 11, and a Unix domain socket path is capped at 104 bytes including the NUL,
/// so any name of about 44+ chars can neither bind nor connect. .NET swallows the failed bind inside its
/// accept loop, so the server believes it is serving while clients get ENOENT. Hence, on macOS only:
/// <list type="number">
/// <item>a rooted name (starts with <c>/</c>) is used verbatim;</item>
/// <item>otherwise the path is <c>&lt;root&gt;/&lt;name&gt;</c> with no prefix, where root is
/// <c>UIPATH_PIPE_ROOT_PATH</c> when set and non-empty, else <see cref="DefaultMacPipeRoot"/>;</item>
/// <item>the path is cut to at most 103 UTF-8 bytes without splitting a character.</item>
/// </list>
/// The TypeScript and Python clients apply the same rule, so all three compute byte-identical paths.
/// The shared test vectors live in <c>src/MacPipePathVectors.json</c>.
/// </para>
/// </summary>
internal static class NamedPipePath
{
    /// <summary>Environment variable that overrides the directory macOS pipes are created in.</summary>
    public const string RootEnvironmentVariable = "UIPATH_PIPE_ROOT_PATH";

    /// <summary>The longest socket path macOS accepts: <c>sun_path</c> is 104 bytes including the terminating NUL.</summary>
    public const int MaxMacPathBytes = 103;

    private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>The name to hand to the pipe stream for <paramref name="pipeName"/> on the current machine.</summary>
    public static string Resolve(string pipeName)
    => IsMacOS
        ? Resolve(pipeName, isMacOS: true, Environment.GetEnvironmentVariable(RootEnvironmentVariable), DefaultMacPipeRoot())
        : pipeName;

    /// <summary>
    /// The pure form of <see cref="Resolve(string)"/>: platform, environment and default root are injected,
    /// so the macOS branch can be unit-tested on any OS. Off macOS the name is returned unchanged.
    /// </summary>
    /// <param name="pipeName">The name the caller configured.</param>
    /// <param name="isMacOS">Whether the macOS rule applies.</param>
    /// <param name="rootOverride">The value of <see cref="RootEnvironmentVariable"/>, or <see langword="null"/> when unset.</param>
    /// <param name="defaultRoot">The directory to use when <paramref name="rootOverride"/> is null or empty.</param>
    internal static string Resolve(string pipeName, bool isMacOS, string? rootOverride, string defaultRoot)
    {
        if (!isMacOS || pipeName.StartsWith("/", StringComparison.Ordinal))
        {
            return pipeName;
        }

        var root = string.IsNullOrEmpty(rootOverride) ? defaultRoot : rootOverride!;
        var separator = root.EndsWith("/", StringComparison.Ordinal) ? "" : "/";
        return TruncateUtf8(root + separator + pipeName, MaxMacPathBytes);
    }

    /// <summary>
    /// The directory macOS pipes live in when <see cref="RootEnvironmentVariable"/> is not set.
    /// For now this is the platform temp directory; a shorter root is being researched separately,
    /// and swapping it in should touch only this method.
    /// </summary>
    internal static string DefaultMacPipeRoot() => Path.GetTempPath();

    /// <summary>Cuts <paramref name="value"/> to at most <paramref name="maxBytes"/> UTF-8 bytes without splitting a character.</summary>
    internal static string TruncateUtf8(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
        {
            return value;
        }

        var cut = maxBytes;
        while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) // step back off continuation bytes to a character boundary
        {
            cut--;
        }
        return Encoding.UTF8.GetString(bytes, 0, cut);
    }
}
