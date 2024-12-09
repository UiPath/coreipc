using AutoFixture;
using AutoFixture.Xunit2;

namespace UiPath.CoreIpc.Tests;

internal class IpcAutoDataAttribute : AutoDataAttribute
{
    public IpcAutoDataAttribute() : base(CreateFixture)
    {
    }

    public static Fixture CreateFixture()
    {
        var fixture = new Fixture();
        new SupportMutableValueTypesCustomization().Customize(fixture);
        return fixture;
    }
}

