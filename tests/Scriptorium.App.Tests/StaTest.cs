using System.Windows.Threading;

namespace Scriptorium.App.Tests;

internal static class StaTest
{
    public static Task Run(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            Exception? failure = null;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await test();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            });
            Dispatcher.Run();

            if (failure is null)
            {
                completion.SetResult();
            }
            else
            {
                completion.SetException(failure);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Completes when the current STA dispatcher reaches application idle.
    /// </summary>
    public static Task DrainDispatcherAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => completion.SetResult()));
        return completion.Task;
    }
}
