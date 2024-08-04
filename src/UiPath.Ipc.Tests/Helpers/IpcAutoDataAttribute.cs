using AutoFixture;
using AutoFixture.Xunit2;

namespace UiPath.Ipc.Tests;

internal class IpcAutoDataAttribute : AutoDataAttribute
{
    public IpcAutoDataAttribute() : base(CreateFixture)
    {
    }

    private static Fixture CreateFixture()
    {
        var fixture = new Fixture();
        return fixture;
    }
}

