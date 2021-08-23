using Nito.AsyncEx;
using System.Threading;
using UiPath.CoreIpc;
using Xunit;

namespace UnitTests
{
    public class CancellationTokenTaskSourceUnitTests
    {
        [Fact]
        public void Constructor_AlreadyCanceledToken_TaskReturnsSynchronouslyCanceledTask()
        {
            var token = new CancellationToken(true);
            using (var source = new CancellationTokenTaskSource(token))
                Assert.True(source.Task.IsCanceled);
        }
    }
}
