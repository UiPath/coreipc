namespace UiPath.Ipc.Tests;

public interface ISlowService
{
    /// <summary>
    /// Returns the <paramref name="value"/> after <paramref name="waitOnServer"/> has elapsed.
    /// </summary>
    /// <remarks>
    /// Deliberately has <b>no</b> <see cref="CancellationToken"/> parameter, so the server's request
    /// timeout cannot stop it and it always runs to completion. This is the shape of the real world
    /// contracts that hit the dropped response bug, such as Studio's <c>IProjectProcessControlService.OpenProject</c>.
    /// </remarks>
    Task<string> EchoAfterIgnoringCancellation(string value, TimeSpan waitOnServer);
}
