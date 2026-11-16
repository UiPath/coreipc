using Newtonsoft.Json;
using System.Runtime.InteropServices;
using System.Text;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

/// <summary>
/// The macOS named-pipe path rule, checked against the vectors in <c>src/MacPipePathVectors.json</c>
/// that the TypeScript and Python suites load too. The platform is injected, so these run on any OS.
/// </summary>
public sealed class NamedPipePathTests
{
    public sealed class Vector
    {
        public string Name { get; set; } = null!;
        public string PipeName { get; set; } = null!;
        public string? RootEnv { get; set; }
        public string DefaultRoot { get; set; } = null!;
        public string Expected { get; set; } = null!;

        public override string ToString() => Name;
    }

    private sealed class VectorFile
    {
        public Vector[] Vectors { get; set; } = null!;
    }

    private static readonly Vector[] Vectors = JsonConvert.DeserializeObject<VectorFile>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MacPipePathVectors.json")))!.Vectors;

    public static IEnumerable<object[]> SharedVectors() => Vectors.Select(vector => new object[] { vector });

    [Theory]
    [MemberData(nameof(SharedVectors))]
    public void OnMac_ThePathMatchesTheSharedVector(Vector vector)
    {
        NamedPipePath.Resolve(vector.PipeName, isMacOS: true, vector.RootEnv, vector.DefaultRoot)
            .ShouldBe(vector.Expected, vector.Name);
    }

    [Theory]
    [MemberData(nameof(SharedVectors))]
    public void OffMac_TheNameIsPassedThroughUnchanged(Vector vector)
    {
        NamedPipePath.Resolve(vector.PipeName, isMacOS: false, vector.RootEnv, vector.DefaultRoot)
            .ShouldBe(vector.PipeName, vector.Name);
    }

    [Fact]
    public void TheSharedVectors_CoverTheWholeRule()
    {
        Vectors.Length.ShouldBeGreaterThanOrEqualTo(8);
        Vectors.ShouldContain(vector => vector.PipeName.StartsWith("/"), "rooted passthrough");
        Vectors.ShouldContain(vector => !string.IsNullOrEmpty(vector.RootEnv), "env override");
        Vectors.ShouldContain(vector => vector.RootEnv == null, "default root");
        Vectors.ShouldContain(vector => Encoding.UTF8.GetByteCount(vector.DefaultRoot + vector.PipeName) > NamedPipePath.MaxMacPathBytes, "truncation");
        Vectors.ShouldContain(vector => Encoding.UTF8.GetByteCount(vector.PipeName) != vector.PipeName.Length, "multi-byte truncation");
    }

    [Fact]
    public void OnMac_PathsFitSunPath_AndCarryNoCoreFxPipePrefix()
    {
        foreach (var vector in Vectors)
        {
            Encoding.UTF8.GetByteCount(vector.Expected).ShouldBeLessThanOrEqualTo(NamedPipePath.MaxMacPathBytes, vector.Name);
            vector.Expected.Contains("CoreFxPipe_").ShouldBeFalse(vector.Name);
        }
    }

    [Fact]
    public void TruncateUtf8_KeepsShortValuesAsIs_AndNeverSplitsACharacter()
    {
        NamedPipePath.TruncateUtf8("abc", 3).ShouldBe("abc");
        NamedPipePath.TruncateUtf8("abcd", 3).ShouldBe("abc");
        NamedPipePath.TruncateUtf8("aé", 2).ShouldBe("a"); // é is 2 bytes
        NamedPipePath.TruncateUtf8("aé", 3).ShouldBe("aé");
        NamedPipePath.TruncateUtf8("a🚀", 4).ShouldBe("a"); // 🚀 is 4 bytes
        NamedPipePath.TruncateUtf8("a🚀", 5).ShouldBe("a🚀");
    }

    [Fact]
    public void DefaultMacPipeRoot_IsTheTempDirectory_ForNow()
    {
        NamedPipePath.DefaultMacPipeRoot().ShouldBe(Path.GetTempPath());
    }

    [SkippableFact]
    public void OffMac_ResolveOnThisMachine_PassesTheNameThrough()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.OSX), "this is the Windows/Linux branch");

        var pipeName = $"ipctest_{Guid.NewGuid():N}_{new string('x', 60)}";
        NamedPipePath.Resolve(pipeName).ShouldBe(pipeName);
    }

    [SkippableFact]
    public void OnMac_ResolveOnThisMachine_UsesTheRootAndFitsSunPath()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.OSX), "this is the macOS branch");

        var pipeName = $"ipctest_{Guid.NewGuid():N}_{new string('x', 60)}";
        var path = NamedPipePath.Resolve(pipeName);

        path.ShouldStartWith(NamedPipePath.DefaultMacPipeRoot());
        path.ShouldNotContain("CoreFxPipe_");
        Encoding.UTF8.GetByteCount(path).ShouldBeLessThanOrEqualTo(NamedPipePath.MaxMacPathBytes);
    }
}
