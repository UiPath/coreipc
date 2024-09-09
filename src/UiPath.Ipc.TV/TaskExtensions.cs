using System.Diagnostics;
using System.Threading.Tasks;

namespace UiPath.Ipc.TV;

internal static class TaskExtensions
{
    public static Task Run(this TaskScheduler taskScheduler, Func<Task> asyncAction)
    => Task.Factory.StartNew(asyncAction, CancellationToken.None, TaskCreationOptions.DenyChildAttach, taskScheduler).Unwrap();

    public static Task Run(this TaskScheduler taskScheduler, Action action)
    => Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.DenyChildAttach, taskScheduler);

    public static void TraceError(this Task task)
    => task.ContinueWith(
        static task => task.Exception!.TraceError(),
        CancellationToken.None,
        TaskContinuationOptions.NotOnRanToCompletion,
        TaskScheduler.Current);

    public static void MessageBoxError(this Task task)
    => task.ContinueWith(
        static task => MessageBox.Show(task.Exception!.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error),
        CancellationToken.None,
        TaskContinuationOptions.NotOnRanToCompletion,
        TaskScheduler.Current);

    public static void TraceError(this Exception exception)
    => Trace.TraceError(exception.ToString());
}
